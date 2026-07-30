using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using TMPro;
using PromptFighters.AI;
using PromptFighters.Audio;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;
using PromptFighters.Utils;

namespace PromptFighters.GameFlow
{
    public partial class PreBattlePanel : MonoBehaviour
    {
        const int CharacterPromptCharacterLimit = 600;

        List<CharacterData> _presets;
        // 固定ボス専用: _presetsからは除外し（P1/P2選択に出さない）、ボス◀/▶セレクター専用のリストにだけ含める。
        const string FixedBossCharacterName = "冥王ゾルバイン";
        List<CharacterData> _bossPresets;
        CharacterData _fixedBossPreset;
        int _builtInPresetCount = 0; // プリセット（初期キャラ）の件数。以降が生成済みキャラ。
        int _p1PresetIdx = 0;
        int _p2PresetIdx = 1;

        // 共有ロスター（スマブラ風キャラ選択グリッド）
        const int RosterColumns = 8;
        const int RosterRows = 4;
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
        Button _p1DeleteButton;
        Button _p2DeleteButton;
        Button _p1VoiceRegenerateButton;
        Button _p2VoiceRegenerateButton;
        Button _p1VoiceGenderButton;
        Button _p2VoiceGenderButton;
        Button _p1VoiceStyleButton;
        Button _p2VoiceStyleButton;
        GameObject _characterSettingsPanel;
        GameObject _p1CharacterSettingsContent;
        GameObject _p2CharacterSettingsContent;
        TextMeshProUGUI _p1CharacterSettingsName;
        TextMeshProUGUI _p2CharacterSettingsName;
        TextMeshProUGUI _p1CharacterSettingsHint;
        TextMeshProUGUI _p2CharacterSettingsHint;
        Coroutine _voiceRegenerationCoroutine;
        TMP_InputField _p1NameInput;
        TMP_InputField _p1FeatureInput;
        TMP_InputField _p1AppearancePerformanceInput;
        readonly TMP_InputField[] _p1SkillInputs = new TMP_InputField[4];
        TMP_InputField _p2NameInput;
        TMP_InputField _p2FeatureInput;
        TMP_InputField _p2AppearancePerformanceInput;
        readonly TMP_InputField[] _p2SkillInputs = new TMP_InputField[4];
        GameObject _p1DetailedInputGroup;
        GameObject _p2DetailedInputGroup;
        Button _p1InputModeButton;
        Button _p2InputModeButton;
        TextMeshProUGUI _p1PromptCountLabel;
        TextMeshProUGUI _p2PromptCountLabel;
        bool _p1DetailedInputMode;
        bool _p2DetailedInputMode;
        bool _enforcingDetailedPromptLimit;
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
        Coroutine _generationVoiceCoroutine;
        readonly Coroutine[] _generationVoiceJobs = new Coroutine[2];
        bool _generationTrainingActive;
        const float GenerationVoiceWatchdogSeconds = 1225f;
        TextMeshProUGUI _debugSkipImageLabel;

        // 生成進捗オーバーレイ（生成中も自由にプレイできるよう、生成画面に移行せず常時表示する）。
        GameObject        _genOverlay;
        TextMeshProUGUI   _genOverlayTitle;
        TextMeshProUGUI[] _genOverlayLines = new TextMeshProUGUI[2];
        readonly string[] _genName    = new string[2];
        readonly int[]    _genPercent  = new int[2];
        readonly bool[]   _genActive   = new bool[2];
        readonly string[] _genResultText = new string[2];
        Coroutine         _genOverlayNoticeCoroutine;

        // このセッションで新しく生成したキャラ名。ロスターで枠を光らせて見分けやすくする。
        readonly System.Collections.Generic.HashSet<string> _newCharNames =
            new System.Collections.Generic.HashSet<string>();

        Image _titleTopGlow;
        Image _titleBottomGlow;
        RectTransform _titleMainRect;
        RectTransform _startButtonRect;
        bool _waitForMenuInputRelease;

        // ゲームパッド左スティック駆動の自前カーソル（1P/2P 専用に2つ）
        const int CursorCount = 2; // 0=1P, 1=2P
        readonly GameObject[]    _gamepadCursor        = new GameObject[CursorCount];
        readonly RectTransform[] _gamepadCursorRect    = new RectTransform[CursorCount];
        readonly Graphic[][]     _cursorGraphics       = new Graphic[CursorCount][];
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
        Image _bossPresetBg; // 固定ボス/おまけボスを背景色で見分けるため

        // ── ステージ選択パネル ──
        GameObject _stageSelectPanel;
        Image[]    _stageCardImages;
        int _bossPresetIdx = 0;
        // 操作説明オーバーレイ
        GameObject _controlsPanel;
        // 設定モーダル（試合前オプションをまとめる）
        GameObject _settingsPanel;
        Slider _bgmVolumeSlider;
        Slider _sfxVolumeSlider;
        Slider _characterVolumeSlider;
        Slider _commentaryVolumeSlider;
        TextMeshProUGUI _startBtnLabel;
        // 対戦中のEsc長押し中断用（誤爆防止のため長押し）
        float _matchEscHold;
        const float MatchEscHoldSeconds = 1.0f;

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
            ExcludeFixedBossFromPresets();
            BuildBossPresets();
            FocusRecoveredCharacterIfAny();
            if (_presets.Count < 2) _p2PresetIdx = 0;
            BuildTitlePanel();
            BuildPanel();
            BuildCharacterSettingsPanel();
            BuildGenerationSetupPanel();
            BuildGeneratingPanel();
            BuildSkillConfirmPanel();
            BuildDeleteConfirmPanel();
            BuildControlsPanel();
            BuildSettingsPanel();
            BuildRevealPanel();
            BuildTutorialPanel();
            EnsureVirtualCursor();
            BuildGenProgressOverlay();
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

        // 対戦（カウントダウン・試合中）にEscを長押しすると試合を中断してキャラ選択へ戻る。
        // 展示で試合を途中で終わらせたい時の脱出手段（誤操作防止のため1秒の長押し）。
        // トレーニングは従来どおりEsc単押しで戻れるため対象外。
        void UpdateMatchEscape()
        {
            var bm = BattleManager.Instance;
            bool inMatch = bm != null && !bm.IsTraining &&
                (bm.Phase == BattlePhase.Fighting || bm.Phase == BattlePhase.Countdown);
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool keyHeld = kb != null && kb.escapeKey.isPressed;
            // ゲームパッドのみの環境（展示会場など）でも中断できるよう、Startボタン長押しにも対応する。
            bool padHeld = false;
            foreach (var gp in UnityEngine.InputSystem.Gamepad.all)
            {
                if (gp.lastUpdateTime <= 0) continue;
                if (gp.startButton.isPressed) { padHeld = true; break; }
            }
            if (!inMatch || (!keyHeld && !padHeld))
            {
                _matchEscHold = 0f;
                return;
            }

            _matchEscHold += Time.unscaledDeltaTime;
            if (_matchEscHold < MatchEscHoldSeconds) return;
            _matchEscHold = 0f;
            bm.ReturnToSetup();
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
            go.AddComponent<GamepadCursorAutoHide>(); // 試合中は自分で表示を消す（保険）
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

            _cursorGraphics[idx] = go.GetComponentsInChildren<Graphic>(true);

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
            if (_gamepadCursor[idx] == null) return;
            _gamepadCursorVisible[idx] = visible;
            // キャッシュ済みのGraphicに毎回適用する（early-returnしない）。
            // 自動非表示(GamepadCursorAutoHide)で消した後でも、メニュー復帰時に確実に再表示できるようにする。
            var gs = _cursorGraphics[idx];
            if (gs == null) return;
            for (int i = 0; i < gs.Length; i++)
                if (gs[i] != null) gs[i].enabled = visible;
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

        // ── 生成進捗オーバーレイ（常時表示。生成中も自由にプレイできる） ──
        void BuildGenProgressOverlay()
        {
            var canvasGo = new GameObject("GenProgressCanvas");
            DontDestroyOnLoad(canvasGo);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 70; // 最前面寄り（HUD/バナーより手前）
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;

            _genOverlay = new GameObject("GenProgressPanel");
            _genOverlay.transform.SetParent(canvasGo.transform, false);
            var rt = _genOverlay.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); // 左上
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(24f, -24f);
            rt.sizeDelta = new Vector2(460f, 132f);

            var bg = _genOverlay.AddComponent<Image>();
            bg.color = new Color(0.02f, 0.025f, 0.04f, 0.78f);
            bg.raycastTarget = false;

            var edge = new GameObject("Edge");
            edge.transform.SetParent(_genOverlay.transform, false);
            var eRt = edge.AddComponent<RectTransform>();
            eRt.anchorMin = new Vector2(0f, 1f); eRt.anchorMax = new Vector2(1f, 1f);
            eRt.pivot = new Vector2(0.5f, 1f); eRt.sizeDelta = new Vector2(0f, 5f);
            eRt.anchoredPosition = Vector2.zero;
            var eImg = edge.AddComponent<Image>();
            eImg.color = PromptFighters.UI.UITheme.Gold; eImg.raycastTarget = false;

            _genOverlayTitle = MakeOverlayLabel(_genOverlay.transform, "Title",
                new Vector2(14f, -8f), new Vector2(432f, 34f), 24f, PromptFighters.UI.UITheme.Gold);
            SetGenOverlayTitleDefault();
            _genOverlayTitle.fontStyle = FontStyles.Bold | FontStyles.Italic;

            _genOverlayLines[0] = MakeOverlayLabel(_genOverlay.transform, "Line0",
                new Vector2(14f, -48f), new Vector2(432f, 32f), 22f, PromptFighters.UI.UITheme.P1Neon);
            _genOverlayLines[1] = MakeOverlayLabel(_genOverlay.transform, "Line1",
                new Vector2(14f, -84f), new Vector2(432f, 32f), 22f, PromptFighters.UI.UITheme.P2Neon);

            _genOverlay.SetActive(false);
        }

        TextMeshProUGUI MakeOverlayLabel(Transform parent, string name,
            Vector2 topLeftPos, Vector2 size, float fontSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeftPos;
            rt.sizeDelta = size;
            var t = go.AddComponent<TextMeshProUGUI>();
            PromptFighters.UI.UITheme.Apply(t, fontSize);
            t.color = color;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        void ShowGenOverlay(bool on)
        {
            if (_genOverlay != null) _genOverlay.SetActive(on);
        }

        void ShowGenerationInProgressNotice()
        {
            ShowGenOverlay(true);
            RefreshGenOverlay();
            if (_genOverlayTitle == null) return;

            if (_genOverlayNoticeCoroutine != null)
                StopCoroutine(_genOverlayNoticeCoroutine);
            _genOverlayTitle.text = "！ 既にキャラ生成中";
            _genOverlayTitle.color = PromptFighters.UI.UITheme.Urgent;
            _genOverlayNoticeCoroutine = StartCoroutine(ResetGenOverlayNotice());
        }

        IEnumerator ResetGenOverlayNotice()
        {
            yield return new WaitForSecondsRealtime(2.5f);
            SetGenOverlayTitleDefault();
            _genOverlayNoticeCoroutine = null;
        }

        void SetGenOverlayTitleDefault()
        {
            if (_genOverlayTitle == null) return;
            _genOverlayTitle.text = "● 生成中...";
            _genOverlayTitle.color = PromptFighters.UI.UITheme.Gold;
        }

        void SetGenProgress(int slot, int percent)
        {
            if (slot < 0 || slot > 1) return;
            _genPercent[slot] = Mathf.Clamp(percent, 0, 100);
            RefreshGenOverlay();
        }

        void RefreshGenOverlay()
        {
            for (int i = 0; i < 2; i++)
            {
                if (_genOverlayLines[i] == null) continue;
                if (_genActive[i])
                {
                    string nm = string.IsNullOrWhiteSpace(_genName[i]) ? (i == 0 ? "1P" : "2P") : _genName[i];
                    _genOverlayLines[i].text = !string.IsNullOrEmpty(_genResultText[i])
                        ? $"{nm}：{_genResultText[i]}"
                        : _genPercent[i] < 0
                        ? $"{nm}：画像生成に失敗（保存されませんでした）"
                        : _genPercent[i] >= 100
                            ? $"{nm}：完了 ✓"
                            : $"{nm}：進行度 {_genPercent[i]}%";
                    _genOverlayLines[i].gameObject.SetActive(true);
                }
                else _genOverlayLines[i].gameObject.SetActive(false);
            }
        }

        // 画像生成の進捗メッセージから完了枚数(0..SpriteCount)を取り出す。取れなければ -1。
        static int ParseImagesDone(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return -1;
            if (msg.Contains("ベース画像"))   return 0;
            if (msg.Contains("バリエーション")) return 1;
            var m = System.Text.RegularExpressions.Regex.Match(msg, @"残り\s*(\d+)\s*枚");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int rem))
                return Mathf.Clamp(CharacterSpriteSet.SpriteCount - rem, 0, CharacterSpriteSet.SpriteCount);
            return -1;
        }

        // 画像完了枚数 → 全体進行度%（テキスト生成完了の10%＋画像で最大98%まで）。
        static int ImagePercent(int done)
            => done < 0 ? -1 : Mathf.Clamp(10 + Mathf.RoundToInt(
                done / (float)CharacterSpriteSet.SpriteCount * 88f), 10, 98);

        void Update()
        {
            UpdateGamepadCursor();
            UpdateMatchEscape();

            // チュートリアル進行中は専用処理へ（他の入力より優先）
            if (_tutorialActive) { UpdateTutorial(); return; }

            // 完成披露の演出中／表示待ちの処理（安全な画面のときだけ割り込む）
            if (UpdateReveal()) return;

            // 削除確認モーダルが開いている間は他の入力を遮断
            if (_deleteConfirmPanel != null && _deleteConfirmPanel.activeSelf)
            {
                // ゲームパッドA/Startはカーソルのクリック・ボタン操作に使うため、直接確定はキーボードのみにする
                // （このモーダルは削除/キャンセルの押せるボタンを持ち、ゲームパッドはカーソル経由で操作する）。
                if (WasKeyboardConfirmPressed()) ConfirmDeleteCharacter();
                else if (WasKeyboardCancelPressed()) HideDeleteConfirm();
                return;
            }

            // キャラ設定モーダルが開いている間は、ロスターや対戦開始へ入力を通さない。
            if (_characterSettingsPanel != null && _characterSettingsPanel.activeSelf)
            {
                if (WasCancelPressed()) HideCharacterSettings();
                return;
            }

            // 設定・操作説明・ステージ選択が開いている間は、裏の画面の入力を遮断する
            // （スペース/エンターでの誤発進や、二重にパネルが開くのを防ぐ）
            if (_settingsPanel != null && _settingsPanel.activeSelf)
            {
                if (WasCancelPressed()) HideSettingsPanel();
                return;
            }
            if (_controlsPanel != null && _controlsPanel.activeSelf)
            {
                if (WasCancelPressed()) HideControlsPanel();
                return;
            }
            if (_stageSelectPanel != null && _stageSelectPanel.activeSelf)
            {
                if (WasCancelPressed()) HideStageSelectPanel();
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
                if (WasGameplayCancelPressed())
                {
                    // 生成中でもメニューへ戻る（生成はバックグラウンド継続、進捗はオーバーレイで表示）。
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
            if (PromptFighters.UI.UITheme.TitleBackground != null)
                PromptFighters.UI.UITheme.ApplyPremiumBackdrop(
                    bg, PromptFighters.UI.UITheme.TitleBackground);
            else
            {
                bg.sprite = CreateGradientSprite(
                    new Color(0.05f, 0.06f, 0.09f, 1f),
                    new Color(0.06f, 0.07f, 0.11f, 1f),
                    new Color(0.012f, 0.014f, 0.022f, 1f),
                    new Color(0.0f, 0.0f, 0.012f, 1f));
                bg.type = Image.Type.Simple;
            }

            var cg = _titlePanel.AddComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;

            // ── 斜めのネオンサイドストライプ（1P青 / 2P赤） ──
            MakeSlantBar(_titlePanel.transform, "P1Stripe",
                new Vector2(-820f, 0f), new Vector2(150f, 1100f),
                new Color(PromptFighters.UI.UITheme.P1Neon.r, PromptFighters.UI.UITheme.P1Neon.g, PromptFighters.UI.UITheme.P1Neon.b, 0.025f), 110f);
            MakeSlantBar(_titlePanel.transform, "P1StripeThin",
                new Vector2(-690f, 0f), new Vector2(26f, 1100f),
                new Color(PromptFighters.UI.UITheme.P1Neon.r, PromptFighters.UI.UITheme.P1Neon.g, PromptFighters.UI.UITheme.P1Neon.b, 0.08f), 110f);
            MakeSlantBar(_titlePanel.transform, "P2Stripe",
                new Vector2(820f, 0f), new Vector2(150f, 1100f),
                new Color(PromptFighters.UI.UITheme.P2Neon.r, PromptFighters.UI.UITheme.P2Neon.g, PromptFighters.UI.UITheme.P2Neon.b, 0.025f), 110f);
            MakeSlantBar(_titlePanel.transform, "P2StripeThin",
                new Vector2(690f, 0f), new Vector2(26f, 1100f),
                new Color(PromptFighters.UI.UITheme.P2Neon.r, PromptFighters.UI.UITheme.P2Neon.g, PromptFighters.UI.UITheme.P2Neon.b, 0.08f), 110f);

            _titleTopGlow = MakePanel(_titlePanel.transform, "TopGlow",
                new Vector2(0, 280), new Vector2(920, 4),
                new Color(PromptFighters.UI.UITheme.P1Neon.r, PromptFighters.UI.UITheme.P1Neon.g, PromptFighters.UI.UITheme.P1Neon.b, 0.24f));
            _titleBottomGlow = MakePanel(_titlePanel.transform, "BottomGlow",
                new Vector2(0, -245), new Vector2(920, 4),
                new Color(PromptFighters.UI.UITheme.P2Neon.r, PromptFighters.UI.UITheme.P2Neon.g, PromptFighters.UI.UITheme.P2Neon.b, 0.22f));

            // ── センターフレーム（スチール地＋ゴールドの斜めエッジ） ──
            var frame = MakePanel(_titlePanel.transform, "CenterFrame",
                new Vector2(0, 4), new Vector2(820, 400), PromptFighters.UI.UITheme.Steel);
            frame.sprite = PromptFighters.UI.UITheme.VGradient;
            frame.type = Image.Type.Simple;
            PromptFighters.UI.UITheme.AddPremiumFrame(frame.transform);
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

            var startButton = MakeButton(_titlePanel.transform, "GameStartBtn", "ゲームスタート",
                new Vector2(0, -110), new Vector2(400, 78), ShowCharacterSelect,
                PromptFighters.UI.UITheme.Gold);
            StyleArcadeButton(startButton, PromptFighters.UI.UITheme.Gold, 18f);
            SetButtonLabelStyle(startButton, 28f, FontStyles.Bold | FontStyles.Italic, Color.white);
            _startButtonRect = startButton.GetComponent<RectTransform>();

            // タイトルはロゴ＋ゲームスタートのみ。設定・操作説明・チュートリアルは
            // ロビー（キャラクター選択画面）に集約する。
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
            MakeSlantBar(t, "CtrlTitleSlash", new Vector2(0, 340f), new Vector2(460f, 54f),
                new Color(PromptFighters.UI.UITheme.GoldDim.r, PromptFighters.UI.UITheme.GoldDim.g, PromptFighters.UI.UITheme.GoldDim.b, 0.30f), 24f);
            MakeLabel(t, "CtrlTitle", "操作説明",
                new Vector2(0, 340f), new Vector2(500f, 60f), 36, PromptFighters.UI.UITheme.Gold)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            // ゲームパッド前提（キーボードは開発用に内部では残すが、画面上の説明には出さない）。
            const string padText =
                "移動　　　　左スティック・十字キー\n" +
                "ジャンプ　　Y / △（空中でもう一度で2段ジャンプ）\n" +
                "急降下　　　左スティックを下（空中）　台すり抜け　下（台の上）\n" +
                "ガード　　　RB・RT 長押し\n" +
                "回避　　　　ガード + 方向入力\n" +
                "技　　　　　B・A・X\n" +
                "スマッシュ　右スティックを倒す\n" +
                "掴み　　　　LB・LT　→ 方向入力で投げ\n\n" +
                "1台目のゲームパッド=1P　2台目=2P　　Start長押し: 試合を中断";

            var card = MakePanel(t, "CtrlCard", new Vector2(0f, 40f), new Vector2(760f, 540f),
                new Color(0.012f, 0.014f, 0.024f, 0.95f));
            card.raycastTarget = false;
            PromptFighters.UI.UITheme.AddPremiumFrame(card.transform);
            var body = MakeLabel(t, "CtrlBody", padText,
                new Vector2(0f, 40f), new Vector2(680f, 480f), 24, PromptFighters.UI.UITheme.Ink);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.lineSpacing = 28f;

            var closeBtn = MakeButton(t, "CtrlCloseBtn", "閉じる",
                new Vector2(0, -320f), new Vector2(240f, 56f), HideControlsPanel,
                PromptFighters.UI.UITheme.Gold);
            StyleArcadeButton(closeBtn, PromptFighters.UI.UITheme.Gold, 14f);
            SetButtonLabelStyle(closeBtn, 22f, FontStyles.Bold | FontStyles.Italic, Color.white);

            _controlsPanel.SetActive(false);
        }

        void ShowControlsPanel()
        {
            if (_controlsPanel != null) _controlsPanel.SetActive(true);
        }

        void HideControlsPanel()
        {
            if (_controlsPanel != null) _controlsPanel.SetActive(false);
        }

        // ── 設定モーダル ─────────────────────────────────────────
        // 試合前オプション（バトルモード・討伐ボス・実況・アイテム・CPU・デバッグ）を1画面にまとめる。
        void BuildSettingsPanel()
        {
            _settingsPanel = CreateUIObject("SettingsOverlay", transform);
            StretchFull(_settingsPanel.GetComponent<RectTransform>());
            _settingsPanel.SetActive(false);

            var dim = _settingsPanel.AddComponent<Image>();
            dim.color = new Color(0.01f, 0.012f, 0.03f, 0.94f);
            var cg = _settingsPanel.AddComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;

            var box = CreateUIObject("SettingsBox", _settingsPanel.transform);
            var bRt = box.GetComponent<RectTransform>();
            bRt.anchoredPosition = Vector2.zero;
            bRt.sizeDelta = new Vector2(960f, 1000f);
            var boxImg = box.AddComponent<Image>();
            boxImg.sprite = PromptFighters.UI.UITheme.VGradient;
            boxImg.type = Image.Type.Simple;
            // Linearカラースペースではα0.99でも裏の明るいロゴが透けるため完全不透明にする
            boxImg.color = new Color(0.05f, 0.055f, 0.08f, 1f);
            PromptFighters.UI.UITheme.AddPremiumFrame(box.transform);

            var t = box.transform;
            MakeSlantBar(t, "SetTop", new Vector2(0f, 498f), new Vector2(960f, 6f),
                PromptFighters.UI.UITheme.Gold, 20f);
            MakeSlantBar(t, "SetBottom", new Vector2(0f, -498f), new Vector2(960f, 6f),
                new Color(PromptFighters.UI.UITheme.Gold.r, PromptFighters.UI.UITheme.Gold.g, PromptFighters.UI.UITheme.Gold.b, 0.55f), -20f);

            MakeSlantBar(t, "SetTitlePlate", new Vector2(0f, 430f), new Vector2(300f, 54f),
                new Color(PromptFighters.UI.UITheme.GoldDim.r, PromptFighters.UI.UITheme.GoldDim.g, PromptFighters.UI.UITheme.GoldDim.b, 0.30f), 20f);
            MakeLabel(t, "SetTitle", "設定",
                new Vector2(0f, 430f), new Vector2(400f, 54f), 34f, PromptFighters.UI.UITheme.Gold)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            const float ToggleFontSize = 19f;

            // ── バトルモード ──
            MakeSettingHeading(t, "ModeHead", "バトルモード", 330f);
            var versusBtn = MakeButton(t, "ModeVersusBtn", "1 vs 1 対戦",
                new Vector2(30f, 330f), new Vector2(240f, 52f), OnSelectVersus, ToggleOnColor);
            StyleArcadeButton(versusBtn, ToggleOnColor, 11f);
            _modeVersusBg    = versusBtn.GetComponent<Image>();
            _modeVersusLabel = versusBtn.GetComponentInChildren<TextMeshProUGUI>();
            _modeVersusLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _modeVersusLabel.fontSize = ToggleFontSize;

            var coopBtn = MakeButton(t, "ModeCoopBtn", "ボス討伐（協力）",
                new Vector2(300f, 330f), new Vector2(260f, 52f), OnSelectCoop, ToggleOnColor);
            StyleArcadeButton(coopBtn, ToggleOnColor, 11f);
            _modeCoopBg    = coopBtn.GetComponent<Image>();
            _modeCoopLabel = coopBtn.GetComponentInChildren<TextMeshProUGUI>();
            _modeCoopLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _modeCoopLabel.fontSize = ToggleFontSize;

            // ── 討伐ボス（協力モード時のみ表示） ──
            BuildBossSelector(t, 250f);

            // ── AI実況 ──
            MakeSettingHeading(t, "CommentaryHead", "AI実況", 160f);
            var commentaryBtn = MakeButton(t, "CommentaryToggle", CommentaryToggleText(),
                new Vector2(30f, 160f), new Vector2(240f, 50f), OnCommentaryToggle, ToggleOnColor);
            StyleArcadeButton(commentaryBtn, ToggleOnColor, 10f);
            _commentaryToggleBg    = commentaryBtn.GetComponent<Image>();
            _commentaryToggleLabel = commentaryBtn.GetComponentInChildren<TextMeshProUGUI>();
            _commentaryToggleLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _commentaryToggleLabel.fontSize = ToggleFontSize;

            // ── ボイスボール ──
            MakeSettingHeading(t, "AngelHead", "ボイスボール", 90f);
            var angelBtn = MakeButton(t, "AngelToggle", AngelToggleText(),
                new Vector2(30f, 90f), new Vector2(240f, 50f), OnAngelToggle, ToggleOnColor);
            StyleArcadeButton(angelBtn, ToggleOnColor, 10f);
            _angelToggleBg    = angelBtn.GetComponent<Image>();
            _angelToggleLabel = angelBtn.GetComponentInChildren<TextMeshProUGUI>();
            _angelToggleLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _angelToggleLabel.fontSize = ToggleFontSize;

            // ── CPU対戦 ──
            MakeSettingHeading(t, "CpuHead", "CPU対戦", 20f);
            var cpuBtn = MakeButton(t, "CpuToggle", CpuToggleText(),
                new Vector2(30f, 20f), new Vector2(240f, 50f), OnCpuToggle, ToggleOnColor);
            StyleArcadeButton(cpuBtn, ToggleOnColor, 10f);
            _cpuToggleBg    = cpuBtn.GetComponent<Image>();
            _cpuToggleLabel = cpuBtn.GetComponentInChildren<TextMeshProUGUI>();
            _cpuToggleLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _cpuToggleLabel.fontSize = ToggleFontSize;

            var cpuSideBtn = MakeButton(t, "CpuSideToggle", CpuSideToggleText(),
                new Vector2(300f, 20f), new Vector2(220f, 50f), OnCpuSideToggle, ToggleOnColor);
            StyleArcadeButton(cpuSideBtn, ToggleOnColor, 10f);
            _cpuSideToggleBg    = cpuSideBtn.GetComponent<Image>();
            _cpuSideToggleLabel = cpuSideBtn.GetComponentInChildren<TextMeshProUGUI>();
            _cpuSideToggleLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _cpuSideToggleLabel.fontSize = ToggleFontSize;

            // ── 音量ミキサー ──
            MakeOutline(t, "SetDivider", new Vector2(0f, -40f), new Vector2(860f, 2f),
                new Color(PromptFighters.UI.UITheme.Gold.r, PromptFighters.UI.UITheme.Gold.g, PromptFighters.UI.UITheme.Gold.b, 0.25f));
            var volumeTitle = MakeLabel(t, "VolumeTitle", "AUDIO MIXER",
                new Vector2(-340f, -70f), new Vector2(220f, 28f), 17f, PromptFighters.UI.UITheme.Gold);
            volumeTitle.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _bgmVolumeSlider = MakeVolumeSlider(t, "BgmVolume", "BGM", -110f,
                GameVolumeSettings.BgmVolume, GameVolumeSettings.SetBgmVolume);
            _sfxVolumeSlider = MakeVolumeSlider(t, "SfxVolume", "効果音", -165f,
                GameVolumeSettings.SfxVolume, GameVolumeSettings.SetSfxVolume);
            _characterVolumeSlider = MakeVolumeSlider(t, "CharacterVolume", "キャラボイス", -220f,
                GameVolumeSettings.CharacterVolume, GameVolumeSettings.SetCharacterVolume);
            _commentaryVolumeSlider = MakeVolumeSlider(t, "CommentaryVolume", "実況ボイス", -275f,
                GameVolumeSettings.CommentaryVolume, GameVolumeSettings.SetCommentaryVolume);

            // ── デバッグ（画像生成スキップ） ──
            MakeOutline(t, "DebugDivider", new Vector2(0f, -315f), new Vector2(860f, 2f),
                new Color(PromptFighters.UI.UITheme.Gold.r, PromptFighters.UI.UITheme.Gold.g, PromptFighters.UI.UITheme.Gold.b, 0.18f));
            MakeSettingHeading(t, "DebugHead", "デバッグ", -355f);
            var debugBtn = MakeButton(t, "DebugSkipImageBtn", "",
                new Vector2(80f, -355f), new Vector2(340f, 44f),
                ToggleSkipImageMode, new Color(0.08f, 0.12f, 0.08f, 1f));
            _debugSkipImageLabel = debugBtn.GetComponentInChildren<TextMeshProUGUI>();
            RefreshDebugSkipLabel();

            // ── 閉じる ──
            var closeBtn = MakeButton(t, "SettingsCloseBtn", "閉じる",
                new Vector2(0f, -445f), new Vector2(260f, 64f), HideSettingsPanel,
                PromptFighters.UI.UITheme.Gold);
            StyleArcadeButton(closeBtn, PromptFighters.UI.UITheme.Gold, 14f);
            SetButtonLabelStyle(closeBtn, 22f, FontStyles.Bold | FontStyles.Italic, Color.white);

            RefreshToggleVisuals();
        }

        // 設定行の見出しラベル（左列・右寄せ）
        TextMeshProUGUI MakeSettingHeading(Transform parent, string name, string text, float y)
        {
            var l = MakeLabel(parent, name, text,
                new Vector2(-290f, y), new Vector2(300f, 40f), 22f, PromptFighters.UI.UITheme.Ink);
            l.alignment = TextAlignmentOptions.Right;
            l.fontStyle = FontStyles.Bold | FontStyles.Italic;
            return l;
        }

        Slider MakeVolumeSlider(Transform parent, string name, string label, float y,
            float initialValue, System.Action<float> onChanged)
        {
            MakeSettingHeading(parent, name + "Heading", label, y);

            var track = CreateUIObject(name + "Slider", parent);
            var trackRt = track.GetComponent<RectTransform>();
            trackRt.anchoredPosition = new Vector2(90f, y);
            trackRt.sizeDelta = new Vector2(360f, 22f);
            var trackImage = track.AddComponent<Image>();
            trackImage.color = new Color(0.035f, 0.04f, 0.065f, 1f);

            var fill = CreateUIObject("Fill", track.transform);
            var fillRt = fill.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(1f, 1f);
            fillRt.offsetMin = new Vector2(3f, 3f);
            fillRt.offsetMax = new Vector2(-3f, -3f);
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = PromptFighters.UI.UITheme.Gold;

            var handle = CreateUIObject("Handle", track.transform);
            var handleRt = handle.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(24f, 36f);
            var handleImage = handle.AddComponent<Image>();
            handleImage.sprite = PromptFighters.UI.UITheme.VGradient;
            handleImage.color = Color.white;

            var slider = track.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImage;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };

            var valueLabel = MakeLabel(parent, name + "Value", "",
                new Vector2(365f, y), new Vector2(100f, 34f), 20f, PromptFighters.UI.UITheme.Gold);
            valueLabel.alignment = TextAlignmentOptions.Left;
            valueLabel.fontStyle = FontStyles.Bold;

            void ApplyValue(float value)
            {
                valueLabel.text = Mathf.RoundToInt(value * 100f) + "%";
                onChanged?.Invoke(value);
            }

            slider.SetValueWithoutNotify(Mathf.Clamp01(initialValue));
            valueLabel.text = Mathf.RoundToInt(slider.value * 100f) + "%";
            slider.onValueChanged.AddListener(ApplyValue);
            return slider;
        }

        void ShowSettingsPanel()
        {
            RefreshToggleVisuals();
            RefreshVolumeSliders();
            if (_settingsPanel != null) _settingsPanel.SetActive(true);
        }

        void HideSettingsPanel()
        {
            GameVolumeSettings.Save();
            if (_settingsPanel != null) _settingsPanel.SetActive(false);
        }

        void RefreshVolumeSliders()
        {
            _bgmVolumeSlider?.SetValueWithoutNotify(GameVolumeSettings.BgmVolume);
            _sfxVolumeSlider?.SetValueWithoutNotify(GameVolumeSettings.SfxVolume);
            _characterVolumeSlider?.SetValueWithoutNotify(GameVolumeSettings.CharacterVolume);
            _commentaryVolumeSlider?.SetValueWithoutNotify(GameVolumeSettings.CommentaryVolume);
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

            var heading = MakeSettingHeading(t, "BossSelectHeading", "討伐ボス", rowY);
            heading.color = PromptFighters.UI.UITheme.Urgent;

            var prevBtn = MakeButton(t, "BossPrev", "◀",
                new Vector2(-40f, rowY), new Vector2(50f, 44f), OnBossPresetPrev, PromptFighters.UI.UITheme.Urgent);
            StyleArcadeButton(prevBtn, PromptFighters.UI.UITheme.Urgent, 8f);
            SetButtonLabelStyle(prevBtn, 20f, FontStyles.Bold, Color.white);

            var nameBtn = MakeButton(t, "BossName", BossPresetText(),
                new Vector2(140f, rowY), new Vector2(290f, 48f), OnBossPresetCycle, ToggleOffColor);
            StyleArcadeButton(nameBtn, ToggleOffColor, 8f);
            _bossPresetLabel = nameBtn.GetComponentInChildren<TextMeshProUGUI>();
            _bossPresetLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _bossPresetLabel.fontSize = 17f;
            _bossPresetBg = nameBtn.GetComponent<Image>();

            var nextBtn = MakeButton(t, "BossNext", "▶",
                new Vector2(320f, rowY), new Vector2(50f, 44f), OnBossPresetCycle, PromptFighters.UI.UITheme.Urgent);
            StyleArcadeButton(nextBtn, PromptFighters.UI.UITheme.Urgent, 8f);
            SetButtonLabelStyle(nextBtn, 20f, FontStyles.Bold, Color.white);
        }

        // 固定ボス（index 0）とおまけボス（既存キャラのボス化）を文言で明示する。
        string BossPresetText() =>
            _bossPresetIdx == 0
                ? $"固定ボス: {GetBossPresetName(_bossPresetIdx)}"
                : $"おまけ: {GetBossPresetName(_bossPresetIdx)}";

        void OnBossPresetCycle()
        {
            if (_bossPresets == null || _bossPresets.Count == 0) return;
            _bossPresetIdx = (_bossPresetIdx + 1) % _bossPresets.Count;
            SyncBossCharacter();
            RefreshToggleVisuals();
        }

        void OnBossPresetPrev()
        {
            if (_bossPresets == null || _bossPresets.Count == 0) return;
            _bossPresetIdx = (_bossPresetIdx - 1 + _bossPresets.Count) % _bossPresets.Count;
            SyncBossCharacter();
            RefreshToggleVisuals();
        }

        // 選択中のボスキャラをBattleManagerへ反映する。
        void SyncBossCharacter()
        {
            if (_bossPresets == null || _bossPresets.Count == 0) return;
            if (_bossPresetIdx < 0 || _bossPresetIdx >= _bossPresets.Count) _bossPresetIdx = 0;
            PromptFighters.Battle.BattleManager.RequestedBossCharacter =
                PromptCharacterFactory.Clone(_bossPresets[_bossPresetIdx]);
        }

        static string CommentaryToggleText() =>
            PromptFighters.UI.CommentaryController.Enabled ? "実況 ON" : "実況 OFF";
        static string AngelToggleText() =>
            PromptFighters.UI.AngelController.Enabled ? "ボイスボール ON" : "ボイスボール OFF";
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

        // ── ステージ選択パネル（バトル開始後に全画面で表示）────────────

        void BuildStageSelectPanel()
        {
            _stageSelectPanel = CreateUIObject("StageSelectPanel", transform);
            StretchFull(_stageSelectPanel.GetComponent<RectTransform>());
            var bg = _stageSelectPanel.AddComponent<Image>();
            // Linearカラースペースではわずかなアルファ漏れでも裏の白文字が強く透けるため不透明にする
            bg.color = new Color(0.01f, 0.012f, 0.025f, 1f);
            if (PromptFighters.UI.UITheme.TitleBackground != null)
                PromptFighters.UI.UITheme.ApplyPremiumBackdrop(bg,
                    PromptFighters.UI.UITheme.TitleBackground, new Color(0.62f, 0.64f, 0.72f, 1f));

            var titleShadow = MakeLabel(_stageSelectPanel.transform, "StageSelectTitleShadow",
                "STAGE SELECT", new Vector2(4f, 446f), new Vector2(900f, 80f), 52f,
                new Color(0f, 0f, 0f, 0.6f));
            titleShadow.fontStyle = FontStyles.Bold | FontStyles.Italic;

            var titleLabel = MakeLabel(_stageSelectPanel.transform, "StageSelectTitle",
                "STAGE SELECT", new Vector2(0f, 450f), new Vector2(900f, 80f), 52f,
                PromptFighters.UI.UITheme.Gold);
            titleLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;

            var n = PromptFighters.Battle.StageRegistry.All.Length;
            _stageCardImages = new Image[n];

            // 上段3枚・下段2枚＋おまかせ（ランダム）の 3x2 レイアウト
            const float CW = 560f, CH = 240f;
            Vector2[] positions =
            {
                new Vector2(-590f,  130f),
                new Vector2(   0f,  130f),
                new Vector2( 590f,  130f),
                new Vector2(-590f, -155f),
                new Vector2(   0f, -155f),
            };
            for (int i = 0; i < n && i < positions.Length; i++)
                BuildStageCard(_stageSelectPanel.transform, i, positions[i], CW, CH);

            BuildRandomStageCard(_stageSelectPanel.transform, new Vector2(590f, -155f), CW, CH);

            var backBtn = MakeButton(_stageSelectPanel.transform, "StageSelectBack", "◀ 戻る",
                new Vector2(-820f, -490f), new Vector2(160f, 50f), HideStageSelectPanel,
                PromptFighters.UI.UITheme.InkDim);
            StyleArcadeButton(backBtn, PromptFighters.UI.UITheme.InkDim, 10f);
            SetButtonLabelStyle(backBtn, 18f, FontStyles.Bold | FontStyles.Italic, Color.white);

            _stageSelectPanel.SetActive(false);
        }

        void BuildStageCard(Transform parent, int idx, Vector2 pos, float cardW, float cardH)
        {
            var def = PromptFighters.Battle.StageRegistry.All[idx];

            int capturedIdx = idx;
            var cardBtn = MakeButton(parent, $"StageCard{idx}", "", pos,
                new Vector2(cardW, cardH), () => OnStageCardClicked(capturedIdx), Color.clear);
            var btnColors = cardBtn.colors;
            btnColors.normalColor      = new Color(1f, 1f, 1f, 0f);
            btnColors.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
            btnColors.pressedColor     = new Color(1f, 1f, 1f, 0.28f);
            cardBtn.colors = btnColors;

            // ステージ背景画像（カード全面）
            var imgGo = CreateUIObject($"StageImg{idx}", cardBtn.transform);
            StretchFull(imgGo.GetComponent<RectTransform>());
            var img = imgGo.AddComponent<Image>();
            img.raycastTarget = false;
            var sprite = Resources.Load<Sprite>(def.backgroundPath);
            if (sprite != null) { img.sprite = sprite; img.color = Color.white; }
            else img.color = new Color(0.08f + idx * 0.04f, 0.06f, 0.14f, 1f);
            _stageCardImages[idx] = img;

            // 下部の暗い帯（名前が読みやすくなるよう）
            var strip = MakePanel(cardBtn.transform, $"StageStrip{idx}",
                new Vector2(0f, -(cardH * 0.5f - 38f)), new Vector2(cardW, 76f),
                new Color(0f, 0f, 0f, 0.80f));
            strip.raycastTarget = false;

            // ステージ名
            var nameLabel = MakeLabel(cardBtn.transform, $"StageName{idx}", def.displayName,
                new Vector2(0f, -(cardH * 0.5f - 37f)), new Vector2(cardW - 16f, 48f),
                20f, Color.white);
            nameLabel.fontStyle     = FontStyles.Bold | FontStyles.Italic;
            nameLabel.raycastTarget = false;

            // 特徴説明（右寄せ小文字）
            string desc = GetStageDescription(idx);
            var descLabel = MakeLabel(cardBtn.transform, $"StageDesc{idx}", desc,
                new Vector2(0f, cardH * 0.5f - 14f), new Vector2(cardW - 16f, 22f),
                11f, new Color(1f, 1f, 1f, 0.7f));
            descLabel.alignment     = TMPro.TextAlignmentOptions.TopRight;
            descLabel.raycastTarget = false;
        }

        void ShowStageSelectPanel(CharacterData d1, CharacterData d2)
        {
            _pendingData1 = d1;
            _pendingData2 = d2;
            if (_stageSelectPanel != null) _stageSelectPanel.SetActive(true);
        }

        void HideStageSelectPanel()
        {
            if (_stageSelectPanel != null) _stageSelectPanel.SetActive(false);
        }

        // おまかせ（ランダム）カード。クリックでステージを抽選してそのまま開始する。
        void BuildRandomStageCard(Transform parent, Vector2 pos, float cardW, float cardH)
        {
            var cardBtn = MakeButton(parent, "StageCardRandom", "", pos,
                new Vector2(cardW, cardH), () => OnStageCardClicked(-1), Color.clear);
            var btnColors = cardBtn.colors;
            btnColors.normalColor      = new Color(1f, 1f, 1f, 0f);
            btnColors.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
            btnColors.pressedColor     = new Color(1f, 1f, 1f, 0.28f);
            cardBtn.colors = btnColors;

            // ダークメタル地＋ゴールドの「?」
            var bgGo = CreateUIObject("RandomBg", cardBtn.transform);
            StretchFull(bgGo.GetComponent<RectTransform>());
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = PromptFighters.UI.UITheme.VGradient;
            bgImg.type = Image.Type.Simple;
            bgImg.color = new Color(0.10f, 0.09f, 0.05f, 1f);
            bgImg.raycastTarget = false;

            var mark = MakeLabel(cardBtn.transform, "RandomMark", "?",
                new Vector2(0f, 24f), new Vector2(200f, 140f), 110f, PromptFighters.UI.UITheme.Gold);
            mark.fontStyle     = FontStyles.Bold | FontStyles.Italic;
            mark.raycastTarget = false;

            var strip = MakePanel(cardBtn.transform, "RandomStrip",
                new Vector2(0f, -(cardH * 0.5f - 38f)), new Vector2(cardW, 76f),
                new Color(0f, 0f, 0f, 0.80f));
            strip.raycastTarget = false;

            var nameLabel = MakeLabel(cardBtn.transform, "RandomName", "おまかせ",
                new Vector2(0f, -(cardH * 0.5f - 37f)), new Vector2(cardW - 16f, 48f),
                20f, PromptFighters.UI.UITheme.Gold);
            nameLabel.fontStyle     = FontStyles.Bold | FontStyles.Italic;
            nameLabel.raycastTarget = false;
        }

        void OnStageCardClicked(int idx)
        {
            if (idx < 0) idx = Random.Range(0, PromptFighters.Battle.StageRegistry.All.Length);
            PromptFighters.Battle.StageRegistry.SelectedIndex = idx;
            HideStageSelectPanel();
            if (BattleManager.Instance == null) return;
            _panel.SetActive(false);
            HideGamepadCursors();
            BattleManager.Instance.StartCountdown(_pendingData1, _pendingData2);
        }

        static string GetStageDescription(int idx) => idx switch
        {
            0 => "台×2  /  スタンダード",
            1 => "台×3  /  中央高台＋左右低台",
            2 => "動く台×2  /  逆位相往復",
            3 => "台×2  /  中央石柱あり",
            4 => "台なし  /  フラット・正面勝負",
            _ => "",
        };

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
            bool cpu = PromptFighters.Battle.FighterAI.Enabled;
            if (_commentaryToggleBg  != null) _commentaryToggleBg.color  = ce ? ToggleOnColor  : ToggleOffColor;
            if (_commentaryToggleLabel != null) _commentaryToggleLabel.text = CommentaryToggleText();
            if (_angelToggleBg       != null) _angelToggleBg.color       = ae ? ToggleOnColor  : ToggleOffColor;
            if (_angelToggleLabel    != null) _angelToggleLabel.text    = AngelToggleText();
            if (_cpuToggleBg         != null) _cpuToggleBg.color         = cpu ? ToggleOnColor : ToggleOffColor;
            if (_cpuToggleLabel      != null) _cpuToggleLabel.text       = CpuToggleText();
            // CPU側トグルはCPU有効時のみ点灯。OFF時はダミー表示（暗色）。
            if (_cpuSideToggleBg     != null) _cpuSideToggleBg.color     = cpu ? ToggleOnColor : ToggleOffColor;
            if (_cpuSideToggleLabel  != null) _cpuSideToggleLabel.text   = CpuSideToggleText();
            bool coop = PromptFighters.Battle.BattleManager.RequestedMode == PromptFighters.Battle.BattleMode.CoopVsBoss;
            if (_modeVersusBg   != null) _modeVersusBg.color = coop ? ToggleOffColor : ToggleOnColor;
            if (_modeCoopBg     != null) _modeCoopBg.color   = coop ? ToggleOnColor  : ToggleOffColor;
            if (_modeVersusLabel != null) _modeVersusLabel.color = coop ? PromptFighters.UI.UITheme.InkDim : Color.white;
            if (_modeCoopLabel   != null) _modeCoopLabel.color   = coop ? Color.white : PromptFighters.UI.UITheme.InkDim;
            if (_bossSelectorRoot != null) _bossSelectorRoot.SetActive(coop);
            if (coop)
            {
                SyncBossCharacter();
                if (_bossPresetLabel != null) _bossPresetLabel.text = BossPresetText();
                // 固定ボスはゴールド、おまけ（既存キャラのボス化）は通常色で見分けられるようにする
                if (_bossPresetBg != null) _bossPresetBg.color = _bossPresetIdx == 0 ? PromptFighters.UI.UITheme.GoldDim : ToggleOffColor;
            }
            // 選択中モードがロビーでも分かるよう、開始ボタンの文言を切り替える
            if (_startBtnLabel != null) _startBtnLabel.text = coop ? "ボス討伐開始" : "バトル開始";
        }

        void BuildPanel()
        {
            _panel = CreateUIObject("PreBattleOverlay", transform);
            StretchFull(_panel.GetComponent<RectTransform>());

            var bg = _panel.AddComponent<Image>();
            if (PromptFighters.UI.UITheme.LobbyBackground != null)
                PromptFighters.UI.UITheme.ApplyPremiumBackdrop(
                    bg, PromptFighters.UI.UITheme.LobbyBackground, new Color(0.78f, 0.82f, 0.9f, 1f));
            else
            {
                bg.sprite = CreateGradientSprite(
                    new Color(0.05f, 0.06f, 0.09f, 1f),
                    new Color(0.06f, 0.07f, 0.11f, 1f),
                    new Color(0.012f, 0.014f, 0.022f, 1f),
                    new Color(0.0f, 0.0f, 0.012f, 1f));
                bg.type = Image.Type.Simple;
            }

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

            // ── 1P エリア（左半分） ──
            BuildPlayerColumn(_panel.transform, true);

            // ── 2P エリア（右半分） ──
            BuildPlayerColumn(_panel.transform, false);

            // ── 共有ロスター（スマブラ風キャラ選択グリッド） ──
            BuildSharedRoster(_panel.transform);

            // ── フッター: ボタン ──
            var startBtn = MakeButton(_panel.transform, "StartBtn", "バトル開始",
                new Vector2(-330, -430), new Vector2(300, 72), OnStartPressed,
                PromptFighters.UI.UITheme.Gold);
            StyleArcadeButton(startBtn, PromptFighters.UI.UITheme.Gold, 16f);
            SetButtonLabelStyle(startBtn, 26f, FontStyles.Bold | FontStyles.Italic, Color.white);
            _startBtnLabel = startBtn.GetComponentInChildren<TextMeshProUGUI>();

            var trainBtn = MakeButton(_panel.transform, "TrainingBtn", "トレーニング",
                new Vector2(0, -430), new Vector2(300, 72), OnTrainingPressed,
                PromptFighters.UI.UITheme.P1Neon);
            StyleArcadeButton(trainBtn, PromptFighters.UI.UITheme.P1Neon, 16f);
            SetButtonLabelStyle(trainBtn, 26f, FontStyles.Bold | FontStyles.Italic, Color.white);

            var genBtn = MakeButton(_panel.transform, "GenerateBtn", "キャラ生成",
                new Vector2(330, -430), new Vector2(300, 72), ShowGenerationSetupPanel,
                PromptFighters.UI.UITheme.P2Neon);
            StyleArcadeButton(genBtn, PromptFighters.UI.UITheme.P2Neon, 16f);
            SetButtonLabelStyle(genBtn, 26f, FontStyles.Bold | FontStyles.Italic, Color.white);

            // ── 設定・操作説明（ヘッダー右） ──
            var settingsBtn = MakeButton(_panel.transform, "SettingsBtn", "設定",
                new Vector2(615f, 522f), new Vector2(180f, 44f), ShowSettingsPanel, ToggleOffColor);
            StyleArcadeButton(settingsBtn, ToggleOffColor, 12f);
            SetButtonLabelStyle(settingsBtn, 18f, FontStyles.Bold | FontStyles.Italic, PromptFighters.UI.UITheme.Ink);

            var helpBtn = MakeButton(_panel.transform, "ControlsBtn", "操作説明",
                new Vector2(820f, 522f), new Vector2(180f, 44f), ShowControlsPanel, ToggleOffColor);
            StyleArcadeButton(helpBtn, ToggleOffColor, 12f);
            SetButtonLabelStyle(helpBtn, 18f, FontStyles.Bold | FontStyles.Italic, PromptFighters.UI.UITheme.Ink);

            // ── はじめての方へ（操作練習）── ヘッダー左に配置
            var tutorialBtn = MakeButton(_panel.transform, "TutorialBtn", "はじめての方へ（操作練習）",
                new Vector2(-720f, 522f), new Vector2(360f, 44f), StartTutorial, PromptFighters.UI.UITheme.P1Neon);
            StyleArcadeButton(tutorialBtn, PromptFighters.UI.UITheme.P1Neon, 12f);
            SetButtonLabelStyle(tutorialBtn, 18f, FontStyles.Bold | FontStyles.Italic, Color.white);

            BuildTrainingPanel();
            BuildStageSelectPanel();
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
            label.overflowMode = TextOverflowModes.Truncate;
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
            PromptFighters.UI.UITheme.AddPremiumFrame(colBg.transform,
                new Color(pColor.r, pColor.g, pColor.b, 0.58f));

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
            pfRt.anchoredPosition = new Vector2(cx - 168f, 124f);
            pfRt.sizeDelta = new Vector2(280f, 350f);
            var pfImg = AddImage(previewFrame, new Color(0.012f, 0.014f, 0.024f, 0.92f));
            pfImg.sprite = PromptFighters.UI.UITheme.VGradient; pfImg.type = Image.Type.Simple;
            PromptFighters.UI.UITheme.AddPremiumFrame(previewFrame.transform,
                new Color(pColor.r, pColor.g, pColor.b, 0.9f));
            MakeSlantBar(previewFrame.transform, "PreviewTop", new Vector2(0f, 173f), new Vector2(280f, 5f), pColor, slant);
            MakeSlantBar(previewFrame.transform, "PreviewBottom", new Vector2(0f, -173f), new Vector2(280f, 5f), pColor, slant);

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
            spRt.anchoredPosition = new Vector2(cx + 168f, 124f);
            spRt.sizeDelta = new Vector2(330f, 350f);
            var spImg = AddImage(statPanel, new Color(0.012f, 0.014f, 0.024f, 0.92f));
            spImg.sprite = PromptFighters.UI.UITheme.VGradient; spImg.type = Image.Type.Simple;
            PromptFighters.UI.UITheme.AddPremiumFrame(statPanel.transform,
                new Color(pColor.r, pColor.g, pColor.b, 0.9f));
            MakeSlantBar(statPanel.transform, "StatTop", new Vector2(0f, 173f), new Vector2(330f, 5f), pColor, slant);

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

            // ランダム選択（キャラ名行の右）
            var randomBtn = MakeButton(parent, isP1 ? "P1RandomBtn" : "P2RandomBtn", "ランダム",
                new Vector2(cx + 330f, 404f), new Vector2(150f, 44f), () => SelectRandomPreset(isP1), pColorDark);
            StyleArcadeButton(randomBtn, pColorDark, isP1 ? 12f : -12f);
            SetButtonLabelStyle(randomBtn, 17f, FontStyles.Bold | FontStyles.Italic, Color.white);

            // 削除・ボイス関連は専用モーダルへ集約し、選択画面の情報密度を抑える。
            var characterSettingsBtn = MakeButton(parent,
                isP1 ? "P1CharacterSettingsBtn" : "P2CharacterSettingsBtn", "キャラ設定",
                new Vector2(cx + 330f, 350f), new Vector2(150f, 42f),
                () => ShowCharacterSettings(isP1), PromptFighters.UI.UITheme.SteelLight);
            StyleArcadeButton(characterSettingsBtn, pColorDark, isP1 ? 10f : -10f);
            SetButtonLabelStyle(characterSettingsBtn, 16f, FontStyles.Bold | FontStyles.Italic, Color.white);

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
            frRt.anchoredPosition = new Vector2(0f, -214f);
            frRt.sizeDelta = new Vector2(1560f, 340f);
            var frImg = AddImage(frame, new Color(0.012f, 0.014f, 0.024f, 0.92f));
            frImg.sprite = PromptFighters.UI.UITheme.VGradient; frImg.type = Image.Type.Simple;
            PromptFighters.UI.UITheme.AddPremiumFrame(frame.transform,
                new Color(PromptFighters.UI.UITheme.Gold.r, PromptFighters.UI.UITheme.Gold.g,
                    PromptFighters.UI.UITheme.Gold.b, 0.72f));
            MakeSlantBar(frame.transform, "RosterTop", new Vector2(0f, 168f), new Vector2(1560f, 4f),
                PromptFighters.UI.UITheme.Gold, 24f);

            var grid = CreateUIObject("RosterGrid", frame.transform);
            var gRt = grid.GetComponent<RectTransform>();
            gRt.anchorMin = Vector2.zero; gRt.anchorMax = Vector2.one;
            // 上部44pxは見出し専用。グリッドと見出しを物理的に分離して重なりを防ぐ。
            gRt.offsetMin = new Vector2(54f, 8f); gRt.offsetMax = new Vector2(-54f, -44f);
            var layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(172f, 62f);
            layout.spacing = new Vector2(7f, 6f);
            layout.padding = new RectOffset(6, 6, 4, 4);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = RosterColumns;
            layout.childAlignment = TextAnchor.UpperCenter;
            _rosterGrid = grid.transform;

            // ヘッダー: タイトルプレート＋ページ表示
            MakeSlantBar(frame.transform, "RosterTitlePlate", new Vector2(-626f, 145f), new Vector2(300f, 34f),
                new Color(PromptFighters.UI.UITheme.Gold.r, PromptFighters.UI.UITheme.Gold.g, PromptFighters.UI.UITheme.Gold.b, 0.28f), 16f);
            MakeLabel(frame.transform, "RosterTitle", "CHARACTER SELECT",
                new Vector2(-626f, 145f), new Vector2(300f, 32f), 19f, Color.white)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            _rosterPageLabel = MakeLabel(frame.transform, "RosterPage", "1 / 1",
                new Vector2(0f, 145f), new Vector2(140f, 30f), 17f, PromptFighters.UI.UITheme.Gold);
            _rosterPageLabel.fontStyle = FontStyles.Bold | FontStyles.Italic;

            // ページ送り（ロスター左右に配置。1ページに収まらない場合のみ機能）
            var prevPage = MakeButton(frame.transform, "RosterPrev", "‹",
                new Vector2(-752f, -10f), new Vector2(44f, 260f), () => ChangeRosterPage(-1),
                PromptFighters.UI.UITheme.P1Neon);
            StyleArcadeButton(prevPage, PromptFighters.UI.UITheme.P1NeonDark, 12f);
            SetButtonLabelStyle(prevPage, 40f, FontStyles.Bold, Color.white);
            var nextPage = MakeButton(frame.transform, "RosterNext", "›",
                new Vector2(752f, -10f), new Vector2(44f, 260f), () => ChangeRosterPage(1),
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

            // 選択色は外周に残し、内側を暗くして顔と名前のコントラストを一定にする。
            var surface = CreateUIObject("Surface", cell.transform);
            var surfaceRt = surface.GetComponent<RectTransform>();
            surfaceRt.anchorMin = Vector2.zero; surfaceRt.anchorMax = Vector2.one;
            surfaceRt.offsetMin = new Vector2(3f, 3f); surfaceRt.offsetMax = new Vector2(-3f, -3f);
            AddImage(surface, new Color(0.025f, 0.03f, 0.045f, 0.98f)).raycastTarget = false;

            // 全身スプライトを大きくして上端だけマスクし、顔～胸元が見えるロスターポートレートにする。
            var viewport = CreateUIObject("FaceViewport", cell.transform);
            var viewportRt = viewport.GetComponent<RectTransform>();
            viewportRt.anchorMin = new Vector2(0f, 0f); viewportRt.anchorMax = new Vector2(1f, 1f);
            viewportRt.offsetMin = new Vector2(5f, 22f); viewportRt.offsetMax = new Vector2(-5f, -4f);
            viewport.AddComponent<RectMask2D>();

            var portraitGo = CreateUIObject("Portrait", viewport.transform);
            var pRt = portraitGo.GetComponent<RectTransform>();
            pRt.anchorMin = pRt.anchorMax = new Vector2(0.5f, 1f);
            pRt.pivot = new Vector2(0.5f, 1f);
            pRt.anchoredPosition = new Vector2(0f, 10f);
            pRt.sizeDelta = new Vector2(164f, 158f);
            var pImg = portraitGo.AddComponent<Image>();
            pImg.sprite = data.characterSprite;
            pImg.preserveAspect = true;
            pImg.raycastTarget = false;
            pImg.color = data.characterSprite != null ? Color.white : new Color(0.35f, 0.38f, 0.45f);

            var nameBand = CreateUIObject("NameBand", cell.transform);
            var nbRt = nameBand.GetComponent<RectTransform>();
            nbRt.anchorMin = new Vector2(0f, 0f); nbRt.anchorMax = new Vector2(1f, 0f);
            nbRt.pivot = new Vector2(0.5f, 0f);
            nbRt.anchoredPosition = new Vector2(0f, 3f);
            nbRt.sizeDelta = new Vector2(-6f, 21f);
            AddImage(nameBand, new Color(0.015f, 0.018f, 0.028f, 0.94f)).raycastTarget = false;

            var nm = MakeLabel(nameBand.transform, "Name", data.characterName,
                Vector2.zero, new Vector2(162f, 20f), 14f, Color.white);
            nm.fontStyle = FontStyles.Bold;
            nm.textWrappingMode = TextWrappingModes.NoWrap;
            nm.overflowMode = TextOverflowModes.Truncate;
            nm.raycastTarget = false;

            // 新しく生成したキャラは枠を光らせて見分けやすくする。
            if (data != null && !string.IsNullOrEmpty(data.characterName)
                && _newCharNames.Contains(data.characterName))
            {
                var glowGo = CreateUIObject("NewGlow", cell.transform);
                var gRt = glowGo.GetComponent<RectTransform>();
                gRt.anchorMin = Vector2.zero; gRt.anchorMax = Vector2.one;
                gRt.offsetMin = Vector2.zero; gRt.offsetMax = Vector2.zero;
                var gImg = glowGo.AddComponent<Image>();
                gImg.color = new Color(PromptFighters.UI.UITheme.Gold.r, PromptFighters.UI.UITheme.Gold.g,
                    PromptFighters.UI.UITheme.Gold.b, 0.22f);
                gImg.raycastTarget = false;
                glowGo.AddComponent<RosterNewGlow>();

                var badge = MakeOverlayLabel(cell.transform, "NewBadge",
                    new Vector2(4f, -2f), new Vector2(70f, 22f), 16f, PromptFighters.UI.UITheme.Gold);
                badge.text = "NEW";
                badge.fontStyle = FontStyles.Bold | FontStyles.Italic;
            }

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
            if (_voiceRegenerationCoroutine != null) return;
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
            if (PromptFighters.UI.UITheme.LobbyBackground != null)
                PromptFighters.UI.UITheme.ApplyPremiumBackdrop(bg,
                    PromptFighters.UI.UITheme.LobbyBackground, new Color(0.68f, 0.72f, 0.82f, 1f));
            else
                bg.sprite = CreateGradientSprite(
                    new Color(0.05f, 0.06f, 0.09f, 1f),
                    new Color(0.06f, 0.07f, 0.11f, 1f),
                    new Color(0.012f, 0.014f, 0.022f, 1f),
                    new Color(0.0f, 0.0f, 0.012f, 1f));

            MakeSlantBar(_generationSetupPanel.transform, "GenSlash", new Vector2(0f, 495f), new Vector2(620f, 52f),
                new Color(PromptFighters.UI.UITheme.GoldDim.r, PromptFighters.UI.UITheme.GoldDim.g, PromptFighters.UI.UITheme.GoldDim.b, 0.30f), 24f);
            MakeLabel(_generationSetupPanel.transform, "GenSetupTitle", "新規キャラクター生成",
                new Vector2(0f, 495f), new Vector2(860f, 56f), 34f, PromptFighters.UI.UITheme.Gold)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            BuildGenerationColumn(_generationSetupPanel.transform, true);
            BuildGenerationColumn(_generationSetupPanel.transform, false);

            var startGen = MakeButton(_generationSetupPanel.transform, "StartGenerateBtn", "生成開始",
                new Vector2(-180f, -485f), new Vector2(280f, 64f), OnGeneratePressed,
                PromptFighters.UI.UITheme.Gold);
            StyleArcadeButton(startGen, PromptFighters.UI.UITheme.Gold, 16f);
            SetButtonLabelStyle(startGen, 23f, FontStyles.Bold | FontStyles.Italic, Color.white);

            var back = MakeButton(_generationSetupPanel.transform, "BackToSelectBtn", "戻る",
                new Vector2(180f, -485f), new Vector2(240f, 64f), ShowCharacterSelect,
                PromptFighters.UI.UITheme.SteelLight);
            StyleArcadeButton(back, PromptFighters.UI.UITheme.SteelLight, 16f);
            SetButtonLabelStyle(back, 20f, FontStyles.Bold | FontStyles.Italic, Color.white);
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
            float cx = isP1 ? -450f : 450f;
            var pColor = isP1 ? PromptFighters.UI.UITheme.P1Neon : PromptFighters.UI.UITheme.P2Neon;
            var pColorDark = isP1 ? PromptFighters.UI.UITheme.P1NeonDark : PromptFighters.UI.UITheme.P2NeonDark;
            float slant = isP1 ? 16f : -16f;

            var genBg = MakePanel(parent, isP1 ? "P1GenBg" : "P2GenBg",
                new Vector2(cx, 0f), new Vector2(850f, 870f),
                new Color(pColorDark.r, pColorDark.g, pColorDark.b, 0.24f));
            genBg.sprite = PromptFighters.UI.UITheme.VGradient; genBg.type = Image.Type.Simple;
            PromptFighters.UI.UITheme.AddPremiumFrame(genBg.transform,
                new Color(pColor.r, pColor.g, pColor.b, 0.92f));
            MakeSlantBar(parent, isP1 ? "P1GenTop" : "P2GenTop",
                new Vector2(cx, 435f), new Vector2(850f, 5f), pColor, slant);
            MakeSlantBar(parent, isP1 ? "P1GenBadgePlate" : "P2GenBadgePlate",
                new Vector2(cx, 399f), new Vector2(140f, 46f), pColor, slant);
            MakeLabel(parent, isP1 ? "P1GenBadge" : "P2GenBadge", isP1 ? "1P" : "2P",
                new Vector2(cx, 399f), new Vector2(140f, 46f), 28f, Color.white)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;
            MakeSlantBar(parent, isP1 ? "P1GenLine" : "P2GenLine",
                new Vector2(cx, 362f), new Vector2(700f, 3f), pColor, slant);

            // 入力文字が読みやすいよう、欄もフォントも大きめにする
            var nameInput = MakeInputField(parent, isP1 ? "P1GenerateNameInput" : "P2GenerateNameInput",
                "キャラクター名（空欄なら選択中のキャラを使用）",
                new Vector2(cx, 318f), new Vector2(790f, 54f), false, 23f);
            var inputModeButton = MakeButton(parent, isP1 ? "P1InputModeBtn" : "P2InputModeBtn",
                "入力形式：まとめて入力", new Vector2(cx - 70f, 267f), new Vector2(360f, 40f),
                () => ToggleCharacterInputMode(isP1), PromptFighters.UI.UITheme.SteelLight);
            StyleArcadeButton(inputModeButton, PromptFighters.UI.UITheme.SteelLight, slant * 0.5f);
            SetButtonLabelStyle(inputModeButton, 17f, FontStyles.Bold, Color.white);
            var promptCountLabel = MakeLabel(parent, isP1 ? "P1PromptCount" : "P2PromptCount",
                "0 / 600字", new Vector2(cx + 300f, 267f), new Vector2(170f, 34f),
                16f, PromptFighters.UI.UITheme.InkDim);
            promptCountLabel.alignment = TextAlignmentOptions.Right;

            // 従来の1欄形式。文字数上限は従来300字から600字へ拡張。
            var featureInput = MakeInputField(parent, isP1 ? "P1GenerateFeatureInput" : "P2GenerateFeatureInput",
                "特徴・見た目・性能・技をまとめて入力（最大600字）\n例: 雷をまとった小柄な剣士。素早く跳び回り、遠距離から雷を飛ばす。",
                new Vector2(cx, 15f), new Vector2(790f, 440f), true, 21f);
            featureInput.onValueChanged.AddListener(_ => RefreshPromptCount(isP1));

            // 項目別形式。見た目・性能と4技を独立した欄で編集する。
            var detailedGroup = CreateUIObject(isP1 ? "P1DetailedInputGroup" : "P2DetailedInputGroup", parent);
            StretchFull(detailedGroup.GetComponent<RectTransform>());

            MakeLabel(detailedGroup.transform, isP1 ? "P1AppearanceLabel" : "P2AppearanceLabel",
                "見た目・性能", new Vector2(cx, 228f), new Vector2(790f, 28f), 19f, pColor)
                .fontStyle = FontStyles.Bold;
            var appearanceInput = MakeInputField(detailedGroup.transform,
                isP1 ? "P1AppearancePerformanceInput" : "P2AppearancePerformanceInput",
                "体格、配色、衣装、武器、速さ、重さ、耐久、得意な間合いなど",
                new Vector2(cx, 170f), new Vector2(790f, 100f), true, 18f);

            string[] skillLabels = { "A  通常技1", "B  通常技2", "X  通常技3", "SMASH  必殺技" };
            string[] skillNames = { "SkillAInput", "SkillBInput", "SkillXInput", "SkillSmashInput" };
            string[] skillHints =
            {
                "A技の特徴を入力　例：前方へ炎の剣を振る",
                "B技の特徴を入力　例：頭上から氷柱を落とす",
                "X技の特徴を入力　例：地上は突進、空中は急降下",
                "SMASH技の特徴を入力　例：巨大な竜を召喚"
            };
            var skillInputs = new TMP_InputField[4];
            for (int i = 0; i < skillInputs.Length; i++)
            {
                bool left = i % 2 == 0;
                bool topRow = i < 2;
                float sx = cx + (left ? -200f : 200f);
                float labelY = topRow ? 105f : -55f;
                float inputY = topRow ? 35f : -125f;
                MakeLabel(detailedGroup.transform,
                    (isP1 ? "P1" : "P2") + skillNames[i] + "Label",
                    skillLabels[i], new Vector2(sx, labelY), new Vector2(380f, 28f), 19f, pColor)
                    .fontStyle = FontStyles.Bold;
                skillInputs[i] = MakeInputField(detailedGroup.transform,
                    (isP1 ? "P1" : "P2") + skillNames[i],
                    skillHints[i], new Vector2(sx, inputY), new Vector2(380f, 124f), true, 17f);
            }
            appearanceInput.onValueChanged.AddListener(_ => EnforceDetailedPromptLimit(isP1, appearanceInput));
            foreach (var skillInput in skillInputs)
            {
                var captured = skillInput;
                captured.onValueChanged.AddListener(_ => EnforceDetailedPromptLimit(isP1, captured));
            }
            detailedGroup.SetActive(false);

            // AIに名前・特徴を考えてもらうボタン（人間が後で編集・確認できる）
            float btnSlant = isP1 ? 14f : -14f;
            var conceptBtn = MakeButton(parent, isP1 ? "P1ConceptBtn" : "P2ConceptBtn",
                "AIで名前・設定を考える", new Vector2(cx - 100f, -260f), new Vector2(430f, 54f),
                () => OnConceptGeneratePressed(isP1), pColor);
            StyleArcadeButton(conceptBtn, pColor, btnSlant);
            SetButtonLabelStyle(conceptBtn, 19f, FontStyles.Bold | FontStyles.Italic, Color.white);

            // 名前・特徴をクリアするリセットボタン
            var resetBtn = MakeButton(parent, isP1 ? "P1ResetBtn" : "P2ResetBtn",
                "リセット", new Vector2(cx + 250f, -260f), new Vector2(150f, 54f),
                () => OnResetConceptPressed(isP1), PromptFighters.UI.UITheme.SteelLight);
            StyleArcadeButton(resetBtn, PromptFighters.UI.UITheme.SteelLight, btnSlant);
            SetButtonLabelStyle(resetBtn, 17f, FontStyles.Bold | FontStyles.Italic, Color.white);

            var conceptStatus = MakeLabel(parent, isP1 ? "P1ConceptStatus" : "P2ConceptStatus",
                "", new Vector2(cx, -310f), new Vector2(790f, 30f), 15f, new Color(0.72f, 0.82f, 0.95f));

            if (isP1)
            {
                _p1NameInput = nameInput;
                _p1FeatureInput = featureInput;
                _p1AppearancePerformanceInput = appearanceInput;
                for (int i = 0; i < skillInputs.Length; i++) _p1SkillInputs[i] = skillInputs[i];
                _p1DetailedInputGroup = detailedGroup;
                _p1InputModeButton = inputModeButton;
                _p1PromptCountLabel = promptCountLabel;
                _p1ConceptButton = conceptBtn;
                _p1ConceptStatus = conceptStatus;
            }
            else
            {
                _p2NameInput = nameInput;
                _p2FeatureInput = featureInput;
                _p2AppearancePerformanceInput = appearanceInput;
                for (int i = 0; i < skillInputs.Length; i++) _p2SkillInputs[i] = skillInputs[i];
                _p2DetailedInputGroup = detailedGroup;
                _p2InputModeButton = inputModeButton;
                _p2PromptCountLabel = promptCountLabel;
                _p2ConceptButton = conceptBtn;
                _p2ConceptStatus = conceptStatus;
            }
            RefreshPromptCount(isP1);
        }

        void ToggleCharacterInputMode(bool isP1)
        {
            bool detailed = !(isP1 ? _p1DetailedInputMode : _p2DetailedInputMode);
            if (isP1) _p1DetailedInputMode = detailed;
            else _p2DetailedInputMode = detailed;
            RefreshCharacterInputMode(isP1);
        }

        void RefreshCharacterInputMode(bool isP1)
        {
            bool detailed = isP1 ? _p1DetailedInputMode : _p2DetailedInputMode;
            var combined = isP1 ? _p1FeatureInput : _p2FeatureInput;
            var detailedGroup = isP1 ? _p1DetailedInputGroup : _p2DetailedInputGroup;
            var button = isP1 ? _p1InputModeButton : _p2InputModeButton;
            if (combined != null) combined.gameObject.SetActive(!detailed);
            if (detailedGroup != null) detailedGroup.SetActive(detailed);
            var label = button != null ? button.GetComponentInChildren<TextMeshProUGUI>() : null;
            if (label != null)
                label.text = detailed ? "入力形式：技ごとに入力" : "入力形式：まとめて入力";
            RefreshPromptCount(isP1);
        }

        void EnforceDetailedPromptLimit(bool isP1, TMP_InputField edited)
        {
            if (_enforcingDetailedPromptLimit || edited == null) return;
            var appearance = isP1 ? _p1AppearancePerformanceInput : _p2AppearancePerformanceInput;
            var skillInputs = isP1 ? _p1SkillInputs : _p2SkillInputs;
            int otherLength = edited == appearance ? 0 : appearance?.text?.Length ?? 0;
            foreach (var input in skillInputs)
            {
                if (input != null && input != edited)
                    otherLength += input.text?.Length ?? 0;
            }
            int allowed = Mathf.Max(0, CharacterPromptCharacterLimit - otherLength);
            if ((edited.text?.Length ?? 0) > allowed)
            {
                _enforcingDetailedPromptLimit = true;
                try
                {
                    edited.SetTextWithoutNotify(edited.text.Substring(0, allowed));
                    edited.caretPosition = edited.text.Length;
                }
                finally
                {
                    _enforcingDetailedPromptLimit = false;
                }
            }
            RefreshPromptCount(isP1);
        }

        void RefreshPromptCount(bool isP1)
        {
            bool detailed = isP1 ? _p1DetailedInputMode : _p2DetailedInputMode;
            int count;
            if (detailed)
            {
                var appearance = isP1 ? _p1AppearancePerformanceInput : _p2AppearancePerformanceInput;
                var skillInputs = isP1 ? _p1SkillInputs : _p2SkillInputs;
                count = appearance?.text?.Length ?? 0;
                foreach (var input in skillInputs)
                    count += input?.text?.Length ?? 0;
            }
            else
            {
                count = (isP1 ? _p1FeatureInput : _p2FeatureInput)?.text?.Length ?? 0;
            }

            var label = isP1 ? _p1PromptCountLabel : _p2PromptCountLabel;
            if (label == null) return;
            label.text = $"{count} / {CharacterPromptCharacterLimit}字";
            label.color = count >= 570
                ? new Color(1f, 0.45f, 0.38f)
                : count >= 480
                    ? new Color(1f, 0.78f, 0.28f)
                    : new Color(0.72f, 0.82f, 0.95f);
        }

        static string ComposeDetailedCharacterPrompt(
            string appearancePerformance, string skillA, string skillB, string skillX, string skillSmash)
        {
            string[] headings = { "【見た目・性能】", "【技A】", "【技B】", "【技X】", "【SMASH】" };
            string[] values =
            {
                (appearancePerformance ?? "").Trim(),
                (skillA ?? "").Trim(),
                (skillB ?? "").Trim(),
                (skillX ?? "").Trim(),
                (skillSmash ?? "").Trim()
            };

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].Length == 0) continue;
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(headings[i]).Append('\n').Append(values[i]);
            }
            return sb.ToString();
        }

        string GetCharacterPrompt(bool isP1)
        {
            bool detailed = isP1 ? _p1DetailedInputMode : _p2DetailedInputMode;
            if (!detailed)
                return (isP1 ? _p1FeatureInput : _p2FeatureInput)?.text ?? string.Empty;
            var skillInputs = isP1 ? _p1SkillInputs : _p2SkillInputs;
            return ComposeDetailedCharacterPrompt(
                (isP1 ? _p1AppearancePerformanceInput : _p2AppearancePerformanceInput)?.text,
                skillInputs[0]?.text, skillInputs[1]?.text, skillInputs[2]?.text, skillInputs[3]?.text);
        }

        // 「AIで名前・特徴を考える」ボタン。AIが原案を出し、入力欄へ流し込む（人間が編集・確認可能）。
        void OnConceptGeneratePressed(bool isP1)
        {
            bool busy = isP1 ? _p1ConceptBusy : _p2ConceptBusy;
            if (busy) return;

            var nameInput    = isP1 ? _p1NameInput    : _p2NameInput;
            var featureInput = isP1 ? _p1FeatureInput : _p2FeatureInput;
            var appearanceInput = isP1 ? _p1AppearancePerformanceInput : _p2AppearancePerformanceInput;
            var skillInputs = isP1 ? _p1SkillInputs : _p2SkillInputs;
            var statusLabel  = isP1 ? _p1ConceptStatus : _p2ConceptStatus;
            var button       = isP1 ? _p1ConceptButton : _p2ConceptButton;
            bool detailed = isP1 ? _p1DetailedInputMode : _p2DetailedInputMode;

            // 名前・特徴の入力状況で生成方向が変わる（双方向）。片方だけ入力ならもう片方を補完する。
            string nameHint = nameInput?.text;
            string featHint = GetCharacterPrompt(isP1);
            bool hadName = !string.IsNullOrWhiteSpace(nameHint);
            bool hadFeat = !string.IsNullOrWhiteSpace(featHint);
            bool oneSided = hadName ^ hadFeat; // 片方だけ入力 → 入力済みの側は尊重して上書きしない
            bool hadAppearance = !string.IsNullOrWhiteSpace(appearanceInput?.text);
            bool[] hadSkills = new bool[skillInputs.Length];
            for (int i = 0; i < skillInputs.Length; i++)
                hadSkills[i] = !string.IsNullOrWhiteSpace(skillInputs[i]?.text);

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
                        && !(oneSided && hadFeat) && !detailed)
                        featureInput.text = concept.features;
                    if (detailed)
                    {
                        // 技別入力は5欄を個別に扱い、ユーザーが書いた欄は保持して空欄だけ補完する。
                        if (appearanceInput != null && !hadAppearance &&
                            string.IsNullOrWhiteSpace(appearanceInput.text))
                            appearanceInput.text = !string.IsNullOrWhiteSpace(concept.appearance_performance)
                                ? concept.appearance_performance
                                : concept.features;
                        string[] generatedSkills =
                        {
                            concept.skill_a, concept.skill_b, concept.skill_x, concept.skill_smash
                        };
                        bool hasSeparatedSkills = System.Array.Exists(
                            generatedSkills, value => !string.IsNullOrWhiteSpace(value));
                        for (int i = 0; i < skillInputs.Length; i++)
                        {
                            if (skillInputs[i] == null || hadSkills[i] ||
                                !string.IsNullOrWhiteSpace(skillInputs[i].text)) continue;
                            skillInputs[i].text = hasSeparatedSkills
                                ? generatedSkills[i] ?? ""
                                : i == 0 ? concept.skill_details ?? "" : "";
                        }
                    }
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
            var appearanceInput = isP1 ? _p1AppearancePerformanceInput : _p2AppearancePerformanceInput;
            var skillInputs = isP1 ? _p1SkillInputs : _p2SkillInputs;
            var statusLabel  = isP1 ? _p1ConceptStatus : _p2ConceptStatus;

            if (nameInput != null) nameInput.text = "";
            if (featureInput != null) featureInput.text = "";
            if (appearanceInput != null) appearanceInput.text = "";
            foreach (var skillInput in skillInputs)
                if (skillInput != null) skillInput.text = "";
            if (statusLabel != null) statusLabel.text = "";
            RefreshPromptCount(isP1);
        }

        void BuildGeneratingPanel()
        {
            _generatingPanel = CreateUIObject("GeneratingOverlay", transform);
            StretchFull(_generatingPanel.GetComponent<RectTransform>());
            _generatingPanel.SetActive(false);

            var bg = _generatingPanel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.9f);
            if (PromptFighters.UI.UITheme.TitleBackground != null)
                PromptFighters.UI.UITheme.ApplyPremiumBackdrop(bg,
                    PromptFighters.UI.UITheme.TitleBackground, new Color(0.34f, 0.36f, 0.42f, 1f));

            var generatingCard = MakePanel(_generatingPanel.transform, "GeneratingCard",
                new Vector2(0f, 5f), new Vector2(900f, 420f),
                new Color(0.015f, 0.018f, 0.03f, 0.94f));
            PromptFighters.UI.UITheme.AddPremiumFrame(generatingCard.transform);
            generatingCard.transform.SetAsFirstSibling();

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
            if (PromptFighters.UI.UITheme.LobbyBackground != null)
                PromptFighters.UI.UITheme.ApplyPremiumBackdrop(bg,
                    PromptFighters.UI.UITheme.LobbyBackground, new Color(0.62f, 0.66f, 0.76f, 1f));
            else
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
            SetButtonLabelStyle(doneBtn, 23f, FontStyles.Bold | FontStyles.Italic, Color.white);
        }

        // キャラ固有の管理操作を選択画面から分離した共用モーダル。
        // 1P/2Pで同じレイアウトを使い、既存の各ボタン参照はボイス生成中の排他制御にも流用する。
        void BuildCharacterSettingsPanel()
        {
            _characterSettingsPanel = CreateUIObject("CharacterSettingsOverlay", transform);
            StretchFull(_characterSettingsPanel.GetComponent<RectTransform>());
            _characterSettingsPanel.SetActive(false);

            var dim = _characterSettingsPanel.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.76f);
            var cg = _characterSettingsPanel.AddComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;

            _p1CharacterSettingsContent = BuildCharacterSettingsContent(_characterSettingsPanel.transform, true);
            _p2CharacterSettingsContent = BuildCharacterSettingsContent(_characterSettingsPanel.transform, false);
            _p1CharacterSettingsContent.SetActive(false);
            _p2CharacterSettingsContent.SetActive(false);
        }

        GameObject BuildCharacterSettingsContent(Transform parent, bool isP1)
        {
            var pColor = isP1 ? PromptFighters.UI.UITheme.P1Neon : PromptFighters.UI.UITheme.P2Neon;
            var pColorDark = isP1 ? PromptFighters.UI.UITheme.P1NeonDark : PromptFighters.UI.UITheme.P2NeonDark;
            float slant = isP1 ? 18f : -18f;

            var box = CreateUIObject(isP1 ? "P1CharacterSettingsBox" : "P2CharacterSettingsBox", parent);
            var bRt = box.GetComponent<RectTransform>();
            bRt.anchoredPosition = Vector2.zero;
            bRt.sizeDelta = new Vector2(700f, 460f);
            var boxImg = box.AddComponent<Image>();
            boxImg.sprite = PromptFighters.UI.UITheme.VGradient;
            boxImg.type = Image.Type.Simple;
            boxImg.color = new Color(0.035f, 0.042f, 0.062f, 0.995f);
            PromptFighters.UI.UITheme.AddPremiumFrame(box.transform,
                new Color(pColor.r, pColor.g, pColor.b, 0.95f));
            MakeSlantBar(box.transform, "SettingsTop", new Vector2(0f, 228f),
                new Vector2(700f, 6f), pColor, slant);

            MakeSlantBar(box.transform, "TitlePlate", new Vector2(0f, 178f),
                new Vector2(380f, 52f), new Color(pColor.r, pColor.g, pColor.b, 0.24f), slant);
            var title = MakeLabel(box.transform, "Title",
                (isP1 ? "1P" : "2P") + "  キャラ設定",
                new Vector2(0f, 178f), new Vector2(620f, 52f), 30f, Color.white);
            title.fontStyle = FontStyles.Bold | FontStyles.Italic;

            var name = MakeLabel(box.transform, "CharacterName", "",
                new Vector2(0f, 124f), new Vector2(620f, 38f), 23f, PromptFighters.UI.UITheme.Gold);
            name.fontStyle = FontStyles.Bold;
            name.enableAutoSizing = true;
            name.fontSizeMin = 15f;
            name.fontSizeMax = 23f;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Truncate;
            if (isP1) _p1CharacterSettingsName = name;
            else _p2CharacterSettingsName = name;

            var hint = MakeLabel(box.transform, "Hint", "",
                new Vector2(0f, 88f), new Vector2(620f, 30f), 14f, PromptFighters.UI.UITheme.InkDim);
            hint.fontStyle = FontStyles.Bold;
            if (isP1) _p1CharacterSettingsHint = hint;
            else _p2CharacterSettingsHint = hint;

            var genderBtn = MakeButton(box.transform,
                isP1 ? "P1VoiceGenderBtn" : "P2VoiceGenderBtn", "性別：中性",
                new Vector2(-145f, 34f), new Vector2(270f, 58f),
                () => CycleSelectedVoiceGender(isP1), PromptFighters.UI.UITheme.SteelLight);
            StyleArcadeButton(genderBtn, pColorDark, slant * 0.55f);
            SetButtonLabelStyle(genderBtn, 18f, FontStyles.Bold, Color.white);
            if (isP1) _p1VoiceGenderButton = genderBtn;
            else _p2VoiceGenderButton = genderBtn;

            var styleBtn = MakeButton(box.transform,
                isP1 ? "P1VoiceStyleBtn" : "P2VoiceStyleBtn", "声質：勇壮",
                new Vector2(145f, 34f), new Vector2(270f, 58f),
                () => CycleSelectedVoiceStyle(isP1), PromptFighters.UI.UITheme.SteelLight);
            StyleArcadeButton(styleBtn, pColorDark, -slant * 0.55f);
            SetButtonLabelStyle(styleBtn, 18f, FontStyles.Bold, Color.white);
            if (isP1) _p1VoiceStyleButton = styleBtn;
            else _p2VoiceStyleButton = styleBtn;

            var voiceBtn = MakeButton(box.transform,
                isP1 ? "P1VoiceRegenerateBtn" : "P2VoiceRegenerateBtn", "ボイス再生成",
                new Vector2(0f, -42f), new Vector2(560f, 60f),
                () => RegenerateSelectedVoice(isP1), pColorDark);
            StyleArcadeButton(voiceBtn, pColorDark, slant * 0.4f);
            SetButtonLabelStyle(voiceBtn, 19f, FontStyles.Bold | FontStyles.Italic, Color.white);
            if (isP1) _p1VoiceRegenerateButton = voiceBtn;
            else _p2VoiceRegenerateButton = voiceBtn;

            var deleteBtn = MakeButton(box.transform,
                isP1 ? "P1DeleteGeneratedBtn" : "P2DeleteGeneratedBtn", "キャラ削除",
                new Vector2(-145f, -126f), new Vector2(270f, 60f),
                () => RequestDeleteCharacter(isP1), PromptFighters.UI.UITheme.Urgent);
            StyleArcadeButton(deleteBtn, PromptFighters.UI.UITheme.Urgent, slant * 0.55f);
            SetButtonLabelStyle(deleteBtn, 18f, FontStyles.Bold | FontStyles.Italic, Color.white);
            if (isP1) _p1DeleteButton = deleteBtn;
            else _p2DeleteButton = deleteBtn;

            var closeBtn = MakeButton(box.transform,
                isP1 ? "P1CharacterSettingsClose" : "P2CharacterSettingsClose", "閉じる",
                new Vector2(145f, -126f), new Vector2(270f, 60f),
                HideCharacterSettings, PromptFighters.UI.UITheme.SteelLight);
            StyleArcadeButton(closeBtn, PromptFighters.UI.UITheme.SteelLight, -slant * 0.55f);
            SetButtonLabelStyle(closeBtn, 18f, FontStyles.Bold | FontStyles.Italic, Color.white);

            MakeLabel(box.transform, "KeyHint", "Esc / B：閉じる",
                new Vector2(0f, -196f), new Vector2(620f, 24f), 13f, PromptFighters.UI.UITheme.InkDim);
            return box;
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
            PromptFighters.UI.UITheme.AddPremiumFrame(box.transform,
                new Color(1f, 0.52f, 0.52f, 1f));

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
            if (_voiceRegenerationCoroutine != null) return;
            if (_presets == null || _presets.Count == 0) return;
            int idx = isP1 ? _p1PresetIdx : _p2PresetIdx;
            int ni = idx + dx + dy * RosterColumns;
            if (ni < 0 || ni >= _presets.Count) return;
            _rosterPage = ni / (RosterColumns * RosterRows);
            SelectPreset(isP1, ni);
        }

        // ランダムにキャラクターを選ぶ。複数いる場合は現在の選択と別のキャラを選ぶ。
        void SelectRandomPreset(bool isP1)
        {
            if (_voiceRegenerationCoroutine != null) return;
            if (_presets == null || _presets.Count == 0) return;
            int cur = isP1 ? _p1PresetIdx : _p2PresetIdx;
            int idx = Random.Range(0, _presets.Count);
            if (_presets.Count > 1 && idx == cur)
                idx = (idx + 1 + Random.Range(0, _presets.Count - 1)) % _presets.Count;
            _rosterPage = idx / (RosterColumns * RosterRows);
            SelectPreset(isP1, idx);
        }

        void SelectPreset(bool isP1, int idx)
        {
            if (_voiceRegenerationCoroutine != null) return;
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
            if (data.voiceProfile?.generated == true)
                sb.AppendLine("<color=#7ED7FF>♪ ボイス：AI生成音声</color>");
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

        string GetPresetName(int idx)
        {
            if (_presets == null || idx < 0 || idx >= _presets.Count) return "---";
            return _presets[idx].characterName;
        }

        // 固定ボスはP1/P2選択（_presets）には出さない（プレイアブル不可のため）。
        void ExcludeFixedBossFromPresets()
        {
            if (_presets == null) return;
            _fixedBossPreset = _presets.Find(c => c != null && c.characterName == FixedBossCharacterName);
            _presets.RemoveAll(c => c != null && c.characterName == FixedBossCharacterName);
        }

        // ボス◀/▶セレクター専用のリスト。_presets（P1/P2用・固定ボス除外済み）に固定ボスを先頭挿入する。
        // 固定ボスが未生成の場合はフォールバックで_presetsと同じ内容になる。
        void BuildBossPresets()
        {
            _bossPresets = new List<CharacterData>(_presets ?? new List<CharacterData>());
            if (_fixedBossPreset != null) _bossPresets.Insert(0, _fixedBossPreset);
        }

        string GetBossPresetName(int idx)
        {
            if (_bossPresets == null || idx < 0 || idx >= _bossPresets.Count) return "---";
            return _bossPresets[idx].characterName;
        }

        void OnStartPressed()
        {
            if (BattleManager.Instance == null) return;
            if (_presets == null || _presets.Count == 0) return;
            if (_voiceRegenerationCoroutine != null)
            {
                Debug.LogWarning("[CharacterVoice] ボイス再生成の完了後に対戦を開始してください。");
                return;
            }

            var data1 = PromptCharacterFactory.Clone(GetPreset(true));
            var data2 = PromptCharacterFactory.Clone(GetPreset(false));
            EnsureSpriteSet(data1);
            EnsureSpriteSet(data2);
            ShowStageSelectPanel(data1, data2);
        }

        void OnGeneratePressed()
        {
            if (BattleManager.Instance == null) return;
            if (_presets == null || _presets.Count == 0) return;
            if (_voiceRegenerationCoroutine != null)
            {
                Debug.LogWarning("[CharacterVoice] ボイス再生成中は新しいキャラクター生成を開始できません。");
                return;
            }
            if (_generationCoroutine != null)
            {
                Debug.LogWarning("[PreBattle] キャラクター生成はすでに進行中です。");
                ShowPanel();
                ShowGenerationInProgressNotice();
                return;
            }

            bool hasP1Input = HasCharacterInput(true);
            bool hasP2Input = HasCharacterInput(false);
            if (!hasP1Input && !hasP2Input) return;

            var preset1 = PromptCharacterFactory.Clone(GetPreset(true));
            var preset2 = PromptCharacterFactory.Clone(GetPreset(false));
            EnsureSpriteSet(preset1);
            EnsureSpriteSet(preset2);
            _generationSetupPanel?.SetActive(false);
            // 生成画面には移行しない。生成はバックグラウンドで進めつつ進捗を常時オーバーレイ表示し、
            // プレイヤーはキャラ選択画面へ戻す（トレーニングや対戦はそこから自由に選べる）。
            _generationCoroutine = StartCoroutine(GenerateBothChars(preset1, preset2, hasP1Input, hasP2Input));
            ShowPanel();
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
            StopGenerationVoiceJobs();
            if (_generationVoiceCoroutine != null)
            {
                StopCoroutine(_generationVoiceCoroutine);
                _generationVoiceCoroutine = null;
            }
            _generationTrainingActive = false;
            _generatingPanel?.SetActive(false);
            ShowGenOverlay(false);
            ShowPanel();
        }

        void StopGenerationVoiceJobs()
        {
            for (int i = 0; i < _generationVoiceJobs.Length; i++)
            {
                if (_generationVoiceJobs[i] != null)
                    StopCoroutine(_generationVoiceJobs[i]);
                _generationVoiceJobs[i] = null;
            }
        }

        IEnumerator GenerateBothChars(CharacterData preset1, CharacterData preset2,
            bool genP1, bool genP2)
        {
            // 対戦開始待ちの _pendingData1/2 とは分離する。生成中にステージ選択や
            // トレーニングへ入っても、使用キャラが生成途中データへ差し替わらないようにする。
            CharacterData generatedData1 = genP1 ? null : preset1;
            CharacterData generatedData2 = genP2 ? null : preset2;
            string error1 = null;
            string error2 = null;
            bool aiOk1 = false;
            bool aiOk2 = false;

            // 進捗オーバーレイ初期化（生成する側だけ表示）
            _genActive[0] = genP1; _genActive[1] = genP2;
            _genName[0] = string.IsNullOrWhiteSpace(_p1NameInput?.text) ? "1P" : _p1NameInput.text.Trim();
            _genName[1] = string.IsNullOrWhiteSpace(_p2NameInput?.text) ? "2P" : _p2NameInput.text.Trim();
            _genPercent[0] = genP1 ? 3 : 0;
            _genPercent[1] = genP2 ? 3 : 0;
            _genResultText[0] = null;
            _genResultText[1] = null;
            ShowGenOverlay(genP1 || genP2);
            RefreshGenOverlay();

            if (genP1)
            {
                UpdateGeneratingStatus("1P キャラクターを生成中...");
                SetGenProgress(0, 5);
                string name1 = _p1NameInput?.text ?? "";
                string feat1 = GetCharacterPrompt(true);
                bool done = false;
                AICharacterClient.Generate(this, name1, feat1,
                    data =>
                    {
                        generatedData1 = data;
                        aiOk1 = true;
                        // 保存・ロスター追加は画像生成が完全に終わってから行う（生成途中のキャラで
                        // 遊べてしまう／画像なしの白ボックスキャラが並ぶのを防ぐ）。
                        // 画像の保存先だけ先に確保する（JSON未保存なので一覧には出ない）。
                        CharacterSaveManager.PrepareDirectory(data);
                        if (!string.IsNullOrWhiteSpace(data.characterName))
                            _genName[0] = data.characterName;
                        SetGenProgress(0, 10);
                        done = true;
                    },
                    err  => { error1 = err; done = true; });
                yield return new WaitUntil(() => done);
            }
            else generatedData1 = preset1;

            if (genP2)
            {
                UpdateGeneratingStatus("2P キャラクターを生成中...");
                SetGenProgress(1, 5);
                string name2 = _p2NameInput?.text ?? "";
                string feat2 = GetCharacterPrompt(false);
                bool done = false;
                AICharacterClient.Generate(this, name2, feat2,
                    data =>
                    {
                        generatedData2 = data;
                        aiOk2 = true;
                        // 1P側と同じく、保存・ロスター追加は画像完成後（保存先の確保のみ先行）
                        CharacterSaveManager.PrepareDirectory(data);
                        if (!string.IsNullOrWhiteSpace(data.characterName))
                            _genName[1] = data.characterName;
                        SetGenProgress(1, 10);
                        done = true;
                    },
                    err  => { error2 = err; done = true; });
                yield return new WaitUntil(() => done);
            }
            else generatedData2 = preset2;

            // 生成失敗時はローカル生成で代替。通信不調でAI生成できなくても「作ろうとしたキャラが
            // 結局残らない」とならないよう、この代替キャラも見た目一式をプリセットから借りて保存する
            // （FinalizeLocalFallbackCharacterで対応。技はローカル簡易生成のものになる）。
            bool usedFallback1 = false, usedFallback2 = false;
            if (generatedData1 == null)
            {
                if (!string.IsNullOrEmpty(error1))
                    Debug.LogWarning("[PreBattle] AI生成失敗: " + error1);
                SetGenerationRestrictionResult(0, error1, usedFallback: true);
                UpdateGeneratingStatus(BuildFallbackMessage(error1));
                generatedData1 = PromptCharacterFactory.Create(
                    _p1NameInput?.text, GetCharacterPrompt(true), preset1);
                usedFallback1 = true; // このブロックはgenP1=trueかつAI生成失敗時のみ通る
                yield return new WaitForSeconds(2.5f);
            }
            if (generatedData2 == null)
            {
                if (!string.IsNullOrEmpty(error2))
                    Debug.LogWarning("[PreBattle] AI生成失敗: " + error2);
                SetGenerationRestrictionResult(1, error2, usedFallback: true);
                generatedData2 = PromptCharacterFactory.Create(
                    _p2NameInput?.text, GetCharacterPrompt(false), preset2);
                usedFallback2 = true; // 同上（genP2=trueかつAI生成失敗時のみ）
            }

            // キャラ設定が揃った時点でボイス生成を開始し、この後の画像21枚生成と並走させる。
            // voices/とsprites/は別ディレクトリなので競合せず、最終JSON保存だけは両方の完了後に行う。
            bool genVoice1 = genP1 && aiOk1;
            bool genVoice2 = genP2 && aiOk2;
            if (genVoice1 || genVoice2)
            {
                UpdateGeneratingStatus("キャラクター画像とボイスを並行生成中...");
                _generationVoiceCoroutine = StartCoroutine(
                    GenerateCharacterVoices(generatedData1, generatedData2, genVoice1, genVoice2));
            }

            // 画像生成はAIキャラ生成が成功した側だけ行う。
            // 画像はローカル生成できないため、キャラ生成が失敗（API不通）した側で
            // 画像生成を試みても無駄に長時間ハングするだけなのでスキップする。
            bool genImg1 = genP1 && aiOk1;
            bool genImg2 = genP2 && aiOk2;
            if ((genImg1 || genImg2) && !DebugSettings.SkipImageGeneration)
            {
                UpdateGeneratingStatus("キャラクター画像を生成中...");
                yield return GenerateImages(generatedData1, generatedData2, genImg1, genImg2);

                // 失敗した側は少し待ってもう1周だけ自動リトライ（一時的なAPIエラーで
                // キャラ生成全体を失敗にしないための保険。クライアント側でもモデル
                // フォールバック済みなので、ここまで来る失敗はほぼ通信起因）。
                bool retry1 = genImg1 && generatedData1?.characterSprite == null && string.IsNullOrEmpty(_genResultText[0]);
                bool retry2 = genImg2 && generatedData2?.characterSprite == null && string.IsNullOrEmpty(_genResultText[1]);
                if (retry1 || retry2)
                {
                    UpdateGeneratingStatus("画像生成をリトライしています...");
                    if (retry1) SetGenProgress(0, 12);
                    if (retry2) SetGenProgress(1, 12);
                    yield return new WaitForSecondsRealtime(5f);
                    yield return GenerateImages(generatedData1, generatedData2, retry1, retry2);
                }
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

            // 画像と並走していたボイスを保存前に合流する。音声だけが失敗した場合は
            // generated=falseのままキャラ本体を保存し、後から個別再生成できるようにする。
            if (_generationVoiceCoroutine != null)
            {
                UpdateGeneratingStatus("画像生成完了・キャラボイスの完了待ち...");
                yield return _generationVoiceCoroutine;
                _generationVoiceCoroutine = null;
                if (genVoice1 && _genPercent[0] >= 0)
                    SetGenProgress(0, Mathf.Max(_genPercent[0], 98));
                if (genVoice2 && _genPercent[1] >= 0)
                    SetGenProgress(1, Mathf.Max(_genPercent[1], 98));
            }

            _generatingPanel?.SetActive(false);

            // 画像まで完成したキャラだけを保存してロスターへ追加する。
            // 画像生成に失敗したキャラは保存せず破棄（白ボックスのキャラが一覧に並ぶのを防ぐ）。
            if (genP1 && aiOk1) FinalizeGeneratedCharacter(generatedData1, true);
            else if (usedFallback1) FinalizeLocalFallbackCharacter(generatedData1, preset1, true);

            if (genP2 && aiOk2) FinalizeGeneratedCharacter(generatedData2, false);
            else if (usedFallback2) FinalizeLocalFallbackCharacter(generatedData2, preset2, false);

            // 生成完了。生成画面へは移行せず、プレイ中の画面はそのままに、
            // オーバーレイを「完了」にして少し見せてから消す（プレイを中断しない）。
            if (genP1 && _genPercent[0] >= 0) SetGenProgress(0, 100);
            if (genP2 && _genPercent[1] >= 0) SetGenProgress(1, 100);
            RefreshGenOverlay();
            _generationTrainingActive = false;

            // キャラ選択画面を開いていれば、一覧を更新して新キャラがすぐ選べるようにする
            //（プレイ中の画面は中断しない）。
            if (_panel != null && _panel.activeSelf) RefreshPresets();

            yield return new WaitForSecondsRealtime(3f);
            ShowGenOverlay(false);
            _generationCoroutine = null;
        }

        IEnumerator GenerateCharacterVoices(CharacterData data1, CharacterData data2,
            bool generateP1, bool generateP2)
        {
            bool done1 = !generateP1;
            bool done2 = !generateP2;
            _generationVoiceJobs[0] = null;
            _generationVoiceJobs[1] = null;

            if (generateP1)
            {
                Coroutine startedJob = CharacterVoiceGenerator.GenerateSet(this, data1,
                    message => UpdateGeneratingStatus("1P " + message),
                    count =>
                    {
                        if (count < 5)
                            Debug.LogWarning($"[CharacterVoice] 1Pは{count}/5件のボイスを保存しました");
                        done1 = true;
                        _generationVoiceJobs[0] = null;
                    });
                // APIキー未設定等では開始呼び出し内で同期完了するため、完了済みhandleを残さない。
                if (!done1) _generationVoiceJobs[0] = startedJob;
            }

            if (generateP2)
            {
                Coroutine startedJob = CharacterVoiceGenerator.GenerateSet(this, data2,
                    message => UpdateGeneratingStatus("2P " + message),
                    count =>
                    {
                        if (count < 5)
                            Debug.LogWarning($"[CharacterVoice] 2Pは{count}/5件のボイスを保存しました");
                        done2 = true;
                        _generationVoiceJobs[1] = null;
                    });
                if (!done2) _generationVoiceJobs[1] = startedJob;
            }

            float deadline = Time.realtimeSinceStartup + GenerationVoiceWatchdogSeconds;
            try
            {
                while ((!done1 || !done2) && Time.realtimeSinceStartup < deadline)
                    yield return null;

                if (!done1)
                {
                    if (_generationVoiceJobs[0] != null) StopCoroutine(_generationVoiceJobs[0]);
                    _generationVoiceJobs[0] = null;
                    if (data1?.voiceProfile != null)
                    {
                        data1.voiceProfile.generated = false;
                        data1.voiceProfile.qualityVersion = 0;
                        data1.voiceProfile.generationId = null;
                    }
                    done1 = true;
                    Debug.LogWarning("[CharacterVoice] 1Pボイス生成が安全期限を超えたため、無音で保存を続行します");
                }
                if (!done2)
                {
                    if (_generationVoiceJobs[1] != null) StopCoroutine(_generationVoiceJobs[1]);
                    _generationVoiceJobs[1] = null;
                    if (data2?.voiceProfile != null)
                    {
                        data2.voiceProfile.generated = false;
                        data2.voiceProfile.qualityVersion = 0;
                        data2.voiceProfile.generationId = null;
                    }
                    done2 = true;
                    Debug.LogWarning("[CharacterVoice] 2Pボイス生成が安全期限を超えたため、無音で保存を続行します");
                }
            }
            finally
            {
                // 親のキャラ生成がキャンセルされた場合も、音声API通信だけを残さない。
                if (!done1 && _generationVoiceJobs[0] != null) StopCoroutine(_generationVoiceJobs[0]);
                if (!done2 && _generationVoiceJobs[1] != null) StopCoroutine(_generationVoiceJobs[1]);
                if (!done1) _generationVoiceJobs[0] = null;
                if (!done2) _generationVoiceJobs[1] = null;
            }
        }

        // AI生成が通信エラー等で失敗し、ローカル生成（PromptCharacterFactory.Create）に切り替わった
        // キャラを保存する。技はローカル簡易生成のものになるが、見た目一式を素材元のプリセットから
        // 借りて自分専用のPNGとして保存することで、次回起動後も同じ見た目のまま名前付きで残るようにする
        // （保存しないと「作ろうとしたキャラが結局残らない」体験になってしまうため）。
        void FinalizeLocalFallbackCharacter(CharacterData data, CharacterData sourcePreset, bool isP1)
        {
            if (data == null) return;
            if (sourcePreset?.spriteSet?.sprites != null && data.spriteSet?.sprites != null)
            {
                int n = Mathf.Min(data.spriteSet.sprites.Length, sourcePreset.spriteSet.sprites.Length);
                for (int i = 0; i < n; i++)
                    data.spriteSet.sprites[i] = sourcePreset.spriteSet.sprites[i];
            }
            CharacterSaveManager.PrepareDirectory(data);
            FinalizeGeneratedCharacter(data, isP1);
        }

        // 生成が完全に成功（テキスト＋画像）したキャラだけを保存し、ロスターへ追加・選択する。
        // 画像が用意できなかった場合は保存せず破棄する（画像なしの白ボックスキャラを一覧に出さない）。
        bool FinalizeGeneratedCharacter(CharacterData data, bool isP1)
        {
            if (data == null) return false;
            int slot = isP1 ? 0 : 1;
            bool imagesOk = DebugSettings.SkipImageGeneration
                || (data.spriteSet != null && data.characterSprite != null);
            if (!imagesOk)
            {
                CharacterSaveManager.DiscardPrepared(data);
                _genPercent[slot] = -1; // オーバーレイに失敗表示
                _genResultText[slot] = "画像生成に失敗（保存されませんでした）";
                Debug.LogWarning($"[PreBattle] 「{data.characterName}」は画像生成に失敗したため保存しませんでした");
                return false;
            }

            if (!DebugSettings.SkipImageGeneration && !CharacterSaveManager.SaveSprites(data))
            {
                CharacterSaveManager.DiscardPrepared(data);
                _genPercent[slot] = -1;
                _genResultText[slot] = "画像保存に失敗（一覧へ追加されませんでした）";
                Debug.LogWarning($"[PreBattle] 「{data.characterName}」は画像ファイルの保存に失敗したため完成扱いにしませんでした");
                return false;
            }

            if (!CharacterSaveManager.Save(data))
            {
                CharacterSaveManager.DiscardPrepared(data);
                _genPercent[slot] = -1;
                _genResultText[slot] = "キャラ保存に失敗（一覧へ追加されませんでした）";
                Debug.LogWarning($"[PreBattle] 「{data.characterName}」は保存に失敗したため完成扱いにしませんでした");
                return false;
            }
            if (!_presets.Contains(data)) _presets.Add(data);
            int newIndex = _presets.IndexOf(data);
            if (isP1) _p1PresetIdx = newIndex;
            else      _p2PresetIdx = newIndex;
            // 一覧は24件単位のページ制。追加後に現在ページを据え置くと、保存件数が多い環境で
            // 「完了したのに一覧にいない」ように見えるため、新キャラのページへ追従する。
            if (newIndex >= 0)
                _rosterPage = newIndex / (RosterColumns * RosterRows);
            if (!string.IsNullOrWhiteSpace(data.characterName))
                _newCharNames.Add(data.characterName);
            // 完成披露の演出キューへ（安全な画面に戻ったタイミングで再生される）
            EnqueueReveal(data);
            SetGenProgress(slot, 100);
            return true;
        }

        IEnumerator GenerateImages(CharacterData data1, CharacterData data2, bool generateP1, bool generateP2)
        {
            // P1/P2の生成ジョブは同時開始する。実際のImages API呼び出しはAIImageClient側の
            // 全キャラ共通レートリミッターが0.30秒間隔で開始し、瞬間的なバーストを防ぐ。
            bool shouldGenerateP1 = generateP1 && data1 != null && !string.IsNullOrEmpty(data1.visualPrompt);
            bool shouldGenerateP2 = generateP2 && data2 != null && !string.IsNullOrEmpty(data2.visualPrompt);
            bool img1Done = !shouldGenerateP1;
            bool img2Done = !shouldGenerateP2;

            if (shouldGenerateP1)
            {
                AIImageClient.GenerateSpriteSet(this, data1,
                    msg =>
                    {
                        UpdateGeneratingStatus("1P " + FormatImageProgress(msg));
                        int p = ImagePercent(ParseImagesDone(msg));
                        if (p >= 0) SetGenProgress(0, p);
                    },
                    sprites =>
                    {
                        data1.spriteSet = sprites;
                        data1.characterSprite = sprites.Get(CharacterSpriteId.Idle1);
                        SetGenProgress(0, 95);
                        img1Done = true;
                    },
                    err =>
                    {
                        Debug.LogWarning("[AIImage] 1P: " + err);
                        SetGenerationRestrictionResult(0, err, usedFallback: false);
                        img1Done = true;
                    },
                    saveDir: data1.spriteDir);
            }

            if (shouldGenerateP2)
            {
                AIImageClient.GenerateSpriteSet(this, data2,
                    msg =>
                    {
                        UpdateGeneratingStatus("2P " + FormatImageProgress(msg));
                        int p = ImagePercent(ParseImagesDone(msg));
                        if (p >= 0) SetGenProgress(1, p);
                    },
                    sprites =>
                    {
                        data2.spriteSet = sprites;
                        data2.characterSprite = sprites.Get(CharacterSpriteId.Idle1);
                        SetGenProgress(1, 95);
                        img2Done = true;
                    },
                    err =>
                    {
                        Debug.LogWarning("[AIImage] 2P: " + err);
                        SetGenerationRestrictionResult(1, err, usedFallback: false);
                        img2Done = true;
                    },
                    saveDir: data2.spriteDir);
            }

            yield return new WaitUntil(() => img1Done && img2Done);
        }

        // 保存済みスプライトがある場合はフルロードする（バトル開始直前に呼ぶ）
        static void EnsureSpriteSet(CharacterData data)
        {
            if (data == null) return;
            CharacterSaveManager.LoadMissingSprites(data);
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
            string restriction = GetGenerationRestrictionReason(error);
            if (!string.IsNullOrEmpty(restriction))
                return restriction + "のため、簡易ローカル生成で保存します（技は簡略化されます）";

            string reason;
            if (string.IsNullOrEmpty(error))                 reason = "AI生成に失敗";
            else if (error.Contains("timeout") || error.Contains("タイムアウト"))
                                                             reason = "AIサーバーが応答しません（通信タイムアウト）";
            else if (error.Contains("APIキー"))               reason = "APIキー未設定";
            else                                             reason = "AI生成に失敗";
            return reason + " — 簡易ローカル生成で保存します（技は簡略化されます）";
        }

        void SetGenerationRestrictionResult(int slot, string error, bool usedFallback)
        {
            if (slot < 0 || slot >= _genResultText.Length) return;
            string restriction = GetGenerationRestrictionReason(error);
            if (string.IsNullOrEmpty(restriction)) return;

            _genResultText[slot] = usedFallback
                ? restriction + "：簡易保存"
                : restriction + "：画像未保存";
            RefreshGenOverlay();
        }

        static string GetGenerationRestrictionReason(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return null;
            string text = error.ToLowerInvariant();
            if (text.Contains("copyright") || text.Contains("trademark") ||
                text.Contains("intellectual property") || text.Contains("著作") ||
                text.Contains("版権") || text.Contains("商標") || text.Contains("既存作品"))
                return "版権・既存作品の制限";
            if (text.Contains("content_policy") || text.Contains("policy_violation") ||
                text.Contains("safety system") || text.Contains("safety policy"))
                return "コンテンツポリシー制限";
            return null;
        }

        // 生成進捗メッセージから画像枚数を解析して "N/全枚数" 表示に変換する（Feature E）
        static string FormatImageProgress(string msg)
        {
            int total = CharacterSpriteSet.SpriteCount;
            if (msg.Contains("残り") && msg.Contains("枚"))
            {
                var m = System.Text.RegularExpressions.Regex.Match(msg, @"残り\s*(\d+)\s*枚");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int rem))
                    return $"画像生成中 {Mathf.Clamp(total - rem, 0, total)}/{total} 完了";
            }
            if (msg.Contains("バリエーション")) return $"画像生成中 1/{total} 完了";
            if (msg.Contains("ベース画像"))    return $"画像生成中 0/{total} 完了";
            return msg;
        }

        void OnTrainingPressed()
        {
            if (BattleManager.Instance == null) return;
            if (_presets == null || _presets.Count == 0) return;
            if (_voiceRegenerationCoroutine != null)
            {
                Debug.LogWarning("[CharacterVoice] ボイス再生成の完了後にトレーニングを開始してください。");
                return;
            }

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
            HideCharacterSettings();
            if (_titlePanel != null) _titlePanel.SetActive(false);
            if (_generationSetupPanel != null) _generationSetupPanel.SetActive(false);
            if (_settingsPanel != null) _settingsPanel.SetActive(false);
            _waitForMenuInputRelease = true;
        }

        // バトルから戻るたびにプリセットを再読み込みし、直前生成キャラを自動選択する
        void RefreshPresets()
        {
            CharacterData selectedP1 = GetPreset(true);
            CharacterData selectedP2 = GetPreset(false);
            string bossName = GetBossPresetName(_bossPresetIdx);
            List<CharacterData> previousPresets = _presets;
            CharacterData previousFixedBoss = _fixedBossPreset;

            var builtIn = PresetCharacterLoader.LoadAll();
            _builtInPresetCount = builtIn.Count;
            _presets = new List<CharacterData>(builtIn);
            // ロビー復帰時は保存キャラ全員の大きなPNGを同期デコードしない。
            // 既に表示済みのIdle1だけを再利用し、新規キャラは表示対象になった時点で非同期ロードする。
            _presets.AddRange(CharacterSaveManager.LoadAll(loadPreviewSprites: false));
            ReuseLobbyPreviewSprites(previousPresets, previousFixedBoss, _presets);
            ExcludeFixedBossFromPresets();
            BuildBossPresets();

            int maxIdx = Mathf.Max(0, _presets.Count - 1);

            int f1 = FindPresetIndex(selectedP1);
            _p1PresetIdx = f1 >= 0 ? f1 : Mathf.Clamp(_p1PresetIdx, 0, maxIdx);

            int f2 = FindPresetIndex(selectedP2);
            _p2PresetIdx = f2 >= 0 ? f2 : Mathf.Clamp(_p2PresetIdx, 0, maxIdx);

            int fb = bossName != "---" ? _bossPresets.FindIndex(c => c.characterName == bossName) : -1;
            _bossPresetIdx = fb >= 0 ? fb : Mathf.Clamp(_bossPresetIdx, 0, Mathf.Max(0, _bossPresets.Count - 1));

            if (_p1PresetLabel != null) _p1PresetLabel.text = GetPresetName(_p1PresetIdx);
            if (_p2PresetLabel != null) _p2PresetLabel.text = GetPresetName(_p2PresetIdx);
            UpdateCategoryLabels();
            RebuildIconGrids();
            RefreshCharacterPreview();
        }

        static void ReuseLobbyPreviewSprites(List<CharacterData> previousPresets,
            CharacterData previousFixedBoss, List<CharacterData> refreshedPresets)
        {
            if (refreshedPresets == null) return;

            var byDirectory = new Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);
            void Remember(CharacterData data)
            {
                if (data == null || data.characterSprite == null || string.IsNullOrEmpty(data.spriteDir)) return;
                byDirectory[data.spriteDir] = data.characterSprite;
            }

            if (previousPresets != null)
                for (int i = 0; i < previousPresets.Count; i++)
                    Remember(previousPresets[i]);
            Remember(previousFixedBoss);

            for (int i = 0; i < refreshedPresets.Count; i++)
            {
                CharacterData data = refreshedPresets[i];
                if (data == null || data.characterSprite != null || string.IsNullOrEmpty(data.spriteDir)) continue;
                if (!byDirectory.TryGetValue(data.spriteDir, out Sprite idle1) || idle1 == null) continue;
                data.characterSprite = idle1;
                data.spriteSet ??= new CharacterSpriteSet();
                data.spriteSet.Set(CharacterSpriteId.Idle1, idle1);
            }
        }

        int FindPresetIndex(CharacterData target)
        {
            if (target == null || _presets == null) return -1;
            if (!string.IsNullOrEmpty(target.spriteDir))
            {
                int byDirectory = _presets.FindIndex(c => c != null &&
                    string.Equals(c.spriteDir, target.spriteDir, System.StringComparison.OrdinalIgnoreCase));
                if (byDirectory >= 0) return byDirectory;
            }

            return _presets.FindIndex(c => c != null &&
                c.characterName == target.characterName && c.spritePath == target.spritePath);
        }

        void FocusRecoveredCharacterIfAny()
        {
            string recoveredSpriteDir = CharacterSaveManager.ConsumeLastRecoveredSpriteDir();
            if (string.IsNullOrEmpty(recoveredSpriteDir) || _presets == null) return;
            int index = _presets.FindIndex(c => c != null &&
                string.Equals(c.spriteDir, recoveredSpriteDir, System.StringComparison.OrdinalIgnoreCase));
            if (index < 0) return;

            _p1PresetIdx = index;
            _rosterPage = index / (RosterColumns * RosterRows);
            if (!string.IsNullOrWhiteSpace(_presets[index].characterName))
                _newCharNames.Add(_presets[index].characterName);
        }

        void UpdateCategoryLabels()
        {
            UpdateCharacterSettingsSummary(true);
            UpdateCharacterSettingsSummary(false);
            if (_p1DeleteButton != null)
                _p1DeleteButton.gameObject.SetActive(_p1PresetIdx >= _builtInPresetCount);
            if (_p2DeleteButton != null)
                _p2DeleteButton.gameObject.SetActive(_p2PresetIdx >= _builtInPresetCount);
            if (_p1VoiceRegenerateButton != null)
            {
                _p1VoiceRegenerateButton.gameObject.SetActive(_p1PresetIdx >= _builtInPresetCount);
                if (_voiceRegenerationCoroutine == null)
                    UpdateVoiceRegenerateButtonText(_p1VoiceRegenerateButton, GetPreset(true));
            }
            if (_p2VoiceRegenerateButton != null)
            {
                _p2VoiceRegenerateButton.gameObject.SetActive(_p2PresetIdx >= _builtInPresetCount);
                if (_voiceRegenerationCoroutine == null)
                    UpdateVoiceRegenerateButtonText(_p2VoiceRegenerateButton, GetPreset(false));
            }
            if (_p1VoiceGenderButton != null)
            {
                _p1VoiceGenderButton.gameObject.SetActive(_p1PresetIdx >= _builtInPresetCount);
                UpdateVoiceGenderButtonText(_p1VoiceGenderButton, GetPreset(true));
            }
            if (_p2VoiceGenderButton != null)
            {
                _p2VoiceGenderButton.gameObject.SetActive(_p2PresetIdx >= _builtInPresetCount);
                UpdateVoiceGenderButtonText(_p2VoiceGenderButton, GetPreset(false));
            }
            if (_p1VoiceStyleButton != null)
            {
                _p1VoiceStyleButton.gameObject.SetActive(_p1PresetIdx >= _builtInPresetCount);
                UpdateVoiceStyleButtonText(_p1VoiceStyleButton, GetPreset(true));
            }
            if (_p2VoiceStyleButton != null)
            {
                _p2VoiceStyleButton.gameObject.SetActive(_p2PresetIdx >= _builtInPresetCount);
                UpdateVoiceStyleButtonText(_p2VoiceStyleButton, GetPreset(false));
            }
        }

        void UpdateCharacterSettingsSummary(bool isP1)
        {
            int idx = isP1 ? _p1PresetIdx : _p2PresetIdx;
            CharacterData data = _presets != null && idx >= 0 && idx < _presets.Count
                ? _presets[idx]
                : null;
            var name = isP1 ? _p1CharacterSettingsName : _p2CharacterSettingsName;
            var hint = isP1 ? _p1CharacterSettingsHint : _p2CharacterSettingsHint;
            if (name != null) name.text = data?.characterName ?? "---";
            if (hint != null)
            {
                bool editable = data != null && idx >= _builtInPresetCount;
                hint.text = editable
                    ? "性別・声質の変更後は、ボイスを再生成すると音声へ反映されます"
                    : "初期キャラクターの削除・ボイス設定変更はできません";
                hint.color = editable
                    ? PromptFighters.UI.UITheme.InkDim
                    : PromptFighters.UI.UITheme.Gold;
            }
        }

        void ShowTrainingPanel()
        {
            // チュートリアル中は通常のトレーニング説明を出さない（チュートリアルUIに一本化）
            if (_tutorialActive) { if (_trainingPanel != null) _trainingPanel.SetActive(false); return; }
            if (_trainingControlsText != null)
                _trainingControlsText.text = BuildTrainingHelpText();
            if (_trainingPanel != null) _trainingPanel.SetActive(true);
        }

        void ShowTitlePanel()
        {
            if (_titlePanel != null) _titlePanel.SetActive(true);
            if (_panel != null) _panel.SetActive(false);
            HideCharacterSettings();
            if (_generationSetupPanel != null) _generationSetupPanel.SetActive(false);
            if (_trainingPanel != null) _trainingPanel.SetActive(false);
            if (_settingsPanel != null) _settingsPanel.SetActive(false);
        }

        void ShowCharacterSelect()
        {
            if (_titlePanel != null) _titlePanel.SetActive(false);
            if (_panel != null) _panel.SetActive(true);
            HideCharacterSettings();
            if (_generationSetupPanel != null) _generationSetupPanel.SetActive(false);
            if (_trainingPanel != null) _trainingPanel.SetActive(false);
            if (_generatingPanel != null) _generatingPanel.SetActive(false);
            if (_skillConfirmPanel != null) _skillConfirmPanel.SetActive(false);
            if (_settingsPanel != null) _settingsPanel.SetActive(false);
        }

        void ShowGenerationSetupPanel()
        {
            if (_generationCoroutine != null)
            {
                Debug.LogWarning("[PreBattle] 生成中のため、新しい生成設定は開きません。");
                ShowPanel();
                ShowGenerationInProgressNotice();
                return;
            }
            if (_titlePanel != null) _titlePanel.SetActive(false);
            if (_panel != null) _panel.SetActive(false);
            HideCharacterSettings();
            if (_generationSetupPanel != null) _generationSetupPanel.SetActive(true);
            if (_trainingPanel != null) _trainingPanel.SetActive(false);
            if (_generatingPanel != null) _generatingPanel.SetActive(false);
            if (_skillConfirmPanel != null) _skillConfirmPanel.SetActive(false);
            if (_settingsPanel != null) _settingsPanel.SetActive(false);
        }

        void ShowCharacterSettings(bool isP1)
        {
            if (_characterSettingsPanel == null) return;
            UpdateCategoryLabels();
            _characterSettingsPanel.SetActive(true);
            if (_p1CharacterSettingsContent != null) _p1CharacterSettingsContent.SetActive(isP1);
            if (_p2CharacterSettingsContent != null) _p2CharacterSettingsContent.SetActive(!isP1);
        }

        void HideCharacterSettings()
        {
            if (_characterSettingsPanel != null) _characterSettingsPanel.SetActive(false);
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
                ? "Start: 生成進行画面に戻る"
                : "Start: キャラ選択に戻る";
            return "移動: 左スティック・十字キー　ジャンプ: Y / △　掴み: LB・LT　ガード・回避: RB・RT + 方向　" +
                   "技: B・A・X　スマッシュ: 右スティック    " +
                   $"{esc}    左スティック押し込み: 位置・HP・技状態をリセット";
        }

        void RegenerateSelectedVoice(bool isP1)
        {
            if (_voiceRegenerationCoroutine != null || _generationCoroutine != null || _presets == null) return;
            int idx = isP1 ? _p1PresetIdx : _p2PresetIdx;
            if (idx < _builtInPresetCount || idx < 0 || idx >= _presets.Count) return;
            if (!AIImageClient.HasConfiguredApiKey(out _))
            {
                Debug.LogWarning("[CharacterVoice] ボイス再生成にはAPIキーが必要です。");
                return;
            }

            _voiceRegenerationCoroutine = StartCoroutine(RegenerateSelectedVoiceCoroutine(
                _presets[idx], isP1 ? _p1VoiceRegenerateButton : _p2VoiceRegenerateButton));
        }

        void CycleSelectedVoiceGender(bool isP1)
        {
            if (_voiceRegenerationCoroutine != null || _generationCoroutine != null || _presets == null) return;
            int idx = isP1 ? _p1PresetIdx : _p2PresetIdx;
            if (idx < _builtInPresetCount || idx < 0 || idx >= _presets.Count) return;

            CharacterData data = _presets[idx];
            data.voiceProfile ??= new CharacterVoiceProfile();
            data.voiceProfile.FillDefaults(data);
            string oldGender = data.voiceProfile.voiceGender;
            string oldPreset = data.voiceProfile.preset;
            string oldInstructions = data.voiceProfile.instructions;
            int oldQualityVersion = data.voiceProfile.qualityVersion;
            bool oldGenerated = data.voiceProfile.generated;
            data.voiceProfile.voiceGender = data.voiceProfile.voiceGender switch
            {
                CharacterVoiceProfile.Male => CharacterVoiceProfile.Female,
                CharacterVoiceProfile.Female => CharacterVoiceProfile.Neutral,
                _ => CharacterVoiceProfile.Male,
            };
            // 旧プロファイルに逆性別の自由記述が残ると新しい指定と衝突するため、演技品質の共通指示へ戻す。
            // WAVは全5件が安全に揃うまで旧セットを維持し、UIでは未反映であることを明示する。
            data.voiceProfile.instructions = CharacterVoiceProfile.DefaultActingInstructions;
            data.voiceProfile.qualityVersion = 0;
            data.voiceProfile.generated = false;
            data.voiceProfile.FillDefaults(data);
            if (!CharacterSaveManager.Save(data))
            {
                data.voiceProfile.voiceGender = oldGender;
                data.voiceProfile.preset = oldPreset;
                data.voiceProfile.instructions = oldInstructions;
                data.voiceProfile.qualityVersion = oldQualityVersion;
                data.voiceProfile.generated = oldGenerated;
                Debug.LogWarning("[CharacterVoice] 声の性別設定を保存できませんでした。");
            }
            UpdateCategoryLabels();
        }

        static void UpdateVoiceGenderButtonText(Button button, CharacterData data)
        {
            if (button == null) return;
            data?.voiceProfile?.FillDefaults(data);
            string gender = data?.voiceProfile?.voiceGender switch
            {
                CharacterVoiceProfile.Male => "男性",
                CharacterVoiceProfile.Female => "女性",
                _ => "中性",
            };
            SetVoiceRegenerateButtonText(button, "性別：" + gender);
        }

        void CycleSelectedVoiceStyle(bool isP1)
        {
            if (_voiceRegenerationCoroutine != null || _generationCoroutine != null || _presets == null) return;
            int idx = isP1 ? _p1PresetIdx : _p2PresetIdx;
            if (idx < _builtInPresetCount || idx < 0 || idx >= _presets.Count) return;

            CharacterData data = _presets[idx];
            data.voiceProfile ??= new CharacterVoiceProfile();
            data.voiceProfile.FillDefaults(data);
            string oldStyle = data.voiceProfile.voiceStyle;
            int oldQualityVersion = data.voiceProfile.qualityVersion;
            bool oldGenerated = data.voiceProfile.generated;
            data.voiceProfile.CycleVoiceStyle();
            data.voiceProfile.qualityVersion = 0;
            data.voiceProfile.generated = false;
            if (!CharacterSaveManager.Save(data))
            {
                data.voiceProfile.voiceStyle = oldStyle;
                data.voiceProfile.qualityVersion = oldQualityVersion;
                data.voiceProfile.generated = oldGenerated;
                Debug.LogWarning("[CharacterVoice] 声質設定を保存できませんでした。");
            }
            UpdateCategoryLabels();
        }

        static void UpdateVoiceStyleButtonText(Button button, CharacterData data)
        {
            if (button == null) return;
            data?.voiceProfile?.FillDefaults(data);
            SetVoiceRegenerateButtonText(button,
                "声質：" + CharacterVoiceProfile.GetVoiceStyleLabel(data?.voiceProfile?.voiceStyle));
        }

        static void UpdateVoiceRegenerateButtonText(Button button, CharacterData data)
        {
            if (button == null) return;
            data?.voiceProfile?.FillDefaults(data);
            bool current = data?.voiceProfile?.generated == true &&
                           data.voiceProfile.qualityVersion >= CharacterVoiceProfile.CurrentQualityVersion;
            SetVoiceRegenerateButtonText(button, current ? "ボイス再生成" : "ボイス要再生成");
        }

        IEnumerator RegenerateSelectedVoiceCoroutine(CharacterData data, Button activeButton)
        {
            try
            {
                if (_p1VoiceRegenerateButton != null) _p1VoiceRegenerateButton.interactable = false;
                if (_p2VoiceRegenerateButton != null) _p2VoiceRegenerateButton.interactable = false;
                if (_p1VoiceGenderButton != null) _p1VoiceGenderButton.interactable = false;
                if (_p2VoiceGenderButton != null) _p2VoiceGenderButton.interactable = false;
                if (_p1VoiceStyleButton != null) _p1VoiceStyleButton.interactable = false;
                if (_p2VoiceStyleButton != null) _p2VoiceStyleButton.interactable = false;
                if (_p1DeleteButton != null) _p1DeleteButton.interactable = false;
                if (_p2DeleteButton != null) _p2DeleteButton.interactable = false;
                SetVoiceRegenerateButtonText(activeButton, "生成中 0/5");

                bool done = false;
                bool succeeded = false;
                int generatedCount = 0;
                string error = null;
                Coroutine regeneration = CharacterVoiceGenerator.RegenerateSetAtomically(this, data,
                    progress =>
                    {
                        // 進捗文字列は "... n/5" を含むため、ボタン幅に収まる末尾だけを表示する。
                        int slash = progress?.IndexOf('/') ?? -1;
                        string count = slash > 0
                            ? progress.Substring(Mathf.Max(0, slash - 1), Mathf.Min(3, progress.Length - Mathf.Max(0, slash - 1)))
                            : "...";
                        SetVoiceRegenerateButtonText(activeButton, "生成中 " + count);
                    },
                    () => CharacterSaveManager.Save(data),
                    (ok, count, err) =>
                    {
                        succeeded = ok;
                        generatedCount = count;
                        error = err;
                        done = true;
                    });
                float watchdogDeadline = Time.realtimeSinceStartup + 1240f;
                while (!done && Time.realtimeSinceStartup < watchdogDeadline)
                    yield return null;
                if (!done)
                {
                    if (regeneration != null) StopCoroutine(regeneration);
                    error = "ボイス再生成が安全期限を超えました";
                    done = true;
                }

                if (succeeded)
                {
                    SetVoiceRegenerateButtonText(activeButton, "ボイス更新完了");
                    RefreshCharacterPreview();
                }
                else
                {
                    SetVoiceRegenerateButtonText(activeButton, "失敗・再試行可");
                    Debug.LogWarning($"[CharacterVoice] ボイス再生成失敗（{generatedCount}/5）: {error}");
                }

                yield return new WaitForSecondsRealtime(2f);
            }
            finally
            {
                SetVoiceRegenerateButtonText(_p1VoiceRegenerateButton, "ボイス再生成");
                SetVoiceRegenerateButtonText(_p2VoiceRegenerateButton, "ボイス再生成");
                if (_p1VoiceRegenerateButton != null) _p1VoiceRegenerateButton.interactable = true;
                if (_p2VoiceRegenerateButton != null) _p2VoiceRegenerateButton.interactable = true;
                if (_p1VoiceGenderButton != null) _p1VoiceGenderButton.interactable = true;
                if (_p2VoiceGenderButton != null) _p2VoiceGenderButton.interactable = true;
                if (_p1VoiceStyleButton != null) _p1VoiceStyleButton.interactable = true;
                if (_p2VoiceStyleButton != null) _p2VoiceStyleButton.interactable = true;
                if (_p1DeleteButton != null) _p1DeleteButton.interactable = true;
                if (_p2DeleteButton != null) _p2DeleteButton.interactable = true;
                _voiceRegenerationCoroutine = null;
                UpdateCategoryLabels();
            }
        }

        static void SetVoiceRegenerateButtonText(Button button, string text)
        {
            var label = button != null ? button.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            if (label != null) label.text = text;
        }

        // 削除ボタン押下。誤削除防止のため即削除せず確認モーダルを開く。
        void RequestDeleteCharacter(bool isP1)
        {
            if (_voiceRegenerationCoroutine != null) return;
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
            string characterName = nameInput != null ? nameInput.text : string.Empty;
            string features = GetCharacterPrompt(isP1);
            return PromptCharacterFactory.Create(characterName, features, fallback);
        }

        bool HasCharacterInput(bool isP1)
        {
            var nameInput = isP1 ? _p1NameInput : _p2NameInput;
            return (nameInput != null && !string.IsNullOrWhiteSpace(nameInput.text)) ||
                   !string.IsNullOrWhiteSpace(GetCharacterPrompt(isP1));
        }

    }
}
