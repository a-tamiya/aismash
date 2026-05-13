using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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
        int _p1PresetIdx = 0;
        int _p2PresetIdx = 1;

        TextMeshProUGUI _p1PresetLabel;
        TextMeshProUGUI _p2PresetLabel;
        Image _p1PreviewImage;
        Image _p2PreviewImage;
        TMP_InputField _p1NameInput;
        TMP_InputField _p1FeatureInput;
        TMP_InputField _p2NameInput;
        TMP_InputField _p2FeatureInput;

        GameObject _titlePanel;
        GameObject _panel;
        GameObject _trainingPanel;

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
        CharacterData _pendingData1;
        CharacterData _pendingData2;
        Coroutine _generationCoroutine;

        Image _titleTopGlow;
        Image _titleBottomGlow;
        RectTransform _titleMainRect;
        RectTransform _startButtonRect;
        bool _waitForMenuInputRelease;

        void Awake()
        {
            EnsureInputSystemUIInputModule();
        }

        void Start()
        {
            // プリセット + 保存済みキャラを合わせてリストを構築（保存済みは初期キャラとして再利用可能）
            _presets = new List<CharacterData>(PresetCharacterLoader.LoadAll());
            var saved = CharacterSaveManager.LoadAll();
            _presets.AddRange(saved);
            if (_presets.Count < 2) _p2PresetIdx = 0;
            BuildTitlePanel();
            BuildPanel();
            BuildGeneratingPanel();
            BuildSkillConfirmPanel();
            UITheme.ApplyAllInScene();
            RefreshCharacterPreview();
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

                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                {
                    ShowCharacterSelect();
                }
            }

            if (_panel != null && _panel.activeSelf)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (_waitForMenuInputRelease)
                {
                    if (kb == null ||
                        (!kb.spaceKey.isPressed && !kb.enterKey.isPressed && !kb.tKey.isPressed))
                        _waitForMenuInputRelease = false;
                    return;
                }

                if (IsEditingText()) return;

                if (kb != null && kb.spaceKey.wasPressedThisFrame)
                {
                    OnStartPressed();
                }
                if (kb != null && kb.tKey.wasPressedThisFrame)
                {
                    OnTrainingPressed();
                }
            }

            if (_trainingPanel != null && _trainingPanel.activeSelf)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
                {
                    BattleManager.Instance?.ReturnToSetup();
                }
                if (kb != null && kb.rKey.wasPressedThisFrame)
                {
                    BattleManager.Instance?.ResetTrainingRound();
                }
            }

            if (_generatingPanel != null && _generatingPanel.activeSelf)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
                    CancelGeneration();
                // 生成中でもTキーでトレーニング（仮データで）
                if (kb != null && kb.tKey.wasPressedThisFrame)
                {
                    CancelGeneration();
                    OnTrainingPressed();
                }
            }

            if (_skillConfirmPanel != null && _skillConfirmPanel.activeSelf)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.spaceKey.wasPressedThisFrame)
                    OnSkillConfirmBattlePressed();
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
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

            MakeLabel(_titlePanel.transform, "Footer",
                "1P: WASD + J/K/L/G    スマッシュ: A/Dはじき+J    2P: 矢印 + テンキー2/3/1/0",
                new Vector2(0, -286), new Vector2(760, 28), 12, new Color(0.76f, 0.82f, 0.9f));
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
                new Vector2(-200, -460), new Vector2(320, 68), OnStartPressed,
                new Color(0.1f, 0.55f, 0.1f, 1f));
            SetButtonLabelStyle(startBtn, 24f, FontStyles.Bold, Color.white);

            var trainBtn = MakeButton(_panel.transform, "TrainingBtn", "トレーニング",
                new Vector2(200, -460), new Vector2(320, 68), OnTrainingPressed,
                new Color(0.1f, 0.25f, 0.65f, 1f));
            SetButtonLabelStyle(trainBtn, 24f, FontStyles.Bold, Color.white);

            MakeLabel(_panel.transform, "StartHelp", "スペースキーでバトル開始 / Tキーでトレーニング",
                new Vector2(0, -505), new Vector2(800, 28), 14, new Color(0.72f, 0.8f, 1f));

            // ── 操作ガイド ──
            MakeLabel(_panel.transform, "CtrlHelp",
                "1P: WASD 移動 / J 基本技A / K 基本技B / L 基本技C / A/Dはじき+J スマッシュ / G つかみ / 左Shift ガード\n" +
                "2P: 矢印キー 移動 / テンキー2 基本技A / 3 基本技B / 1 基本技C / ←/→はじき+2 スマッシュ / 0 つかみ / 右Shift ガード",
                new Vector2(0, 540 - 810), new Vector2(840, 52), 13, new Color(0.72f, 0.78f, 0.92f));

            BuildTrainingPanel();
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

            // カラーライン
            MakeOutline(parent, isP1 ? "P1Line" : "P2Line",
                new Vector2(cx, 310f), new Vector2(280f, 3f), pColor);

            // プリセット選択行
            var row = CreateUIObject(isP1 ? "P1Row" : "P2Row", parent);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = new Vector2(cx, 240f);
            rowRt.sizeDelta = new Vector2(380f, 52f);

            MakeButton(row.transform, "Left", "◀", new Vector2(-160f, 0f), new Vector2(48f, 48f),
                isP1
                    ? () => ChangePreset(ref _p1PresetIdx, -1, _p1PresetLabel)
                    : () => ChangePreset(ref _p2PresetIdx, -1, _p2PresetLabel),
                new Color(0.2f, 0.2f, 0.35f));

            var label = MakeLabel(row.transform, "Preset",
                isP1 ? GetPresetName(_p1PresetIdx) : GetPresetName(_p2PresetIdx),
                new Vector2(0f, 0f), new Vector2(250f, 48f), 18, Color.white);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;

            MakeButton(row.transform, "Right", "▶", new Vector2(160f, 0f), new Vector2(48f, 48f),
                isP1
                    ? () => ChangePreset(ref _p1PresetIdx, +1, _p1PresetLabel)
                    : () => ChangePreset(ref _p2PresetIdx, +1, _p2PresetLabel),
                new Color(0.2f, 0.2f, 0.35f));

            if (isP1) _p1PresetLabel = label;
            else       _p2PresetLabel = label;

            var previewFrame = CreateUIObject(isP1 ? "P1PreviewFrame" : "P2PreviewFrame", parent);
            var pfRt = previewFrame.GetComponent<RectTransform>();
            pfRt.anchoredPosition = new Vector2(cx, 18f);
            pfRt.sizeDelta = new Vector2(300f, 300f);
            AddImage(previewFrame, new Color(0.01f, 0.012f, 0.02f, 0.72f));
            MakeOutline(previewFrame.transform, "PreviewTop", new Vector2(0f, 148f), new Vector2(300f, 3f), pColor);

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

            // キャラ情報エリア
            MakeLabel(parent, isP1 ? "P1InfoTitle" : "P2InfoTitle",
                "キャラクター",
                new Vector2(cx, 178f), new Vector2(300f, 30f), 14,
                new Color(0.72f, 0.8f, 1f));

            var nameInput = MakeInputField(parent, isP1 ? "P1NameInput" : "P2NameInput",
                "キャラクター名", new Vector2(cx, -168f), new Vector2(360f, 44f), false);
            var featureInput = MakeInputField(parent, isP1 ? "P1FeatureInput" : "P2FeatureInput",
                "特徴を入力", new Vector2(cx, -246f), new Vector2(360f, 96f), true);

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
            MakeLabel(_trainingPanel.transform, "TrainingControls",
                "1P: WASD 移動 / J K L 技 / A/Dはじき+J スマッシュ / G つかみ / 左Shift ガード    2P: 矢印 移動 / テンキー2 3 1 技 / ←/→はじき+2 スマッシュ / 0 つかみ / 右Shift ガード\n" +
                "Escキー: キャラ選択に戻る    Rキー: 位置・HP・技状態をリセット",
                new Vector2(0, 440), new Vector2(900, 52), 14, new Color(0.9f, 0.95f, 1f));
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
                "Tキー: トレーニングモードで練習しながら待つ　Esc: キャンセル",
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
                new Vector2(0, 480), new Vector2(700, 52), 30f, new Color(1f, 0.88f, 0.3f));

            // 中央仕切り
            MakeOutline(_skillConfirmPanel.transform, "Divider",
                new Vector2(0, 0), new Vector2(3, 900), new Color(1f, 1f, 1f, 0.1f));

            // 1P 列（左）
            float lx = -440f;
            MakeLabel(_skillConfirmPanel.transform, "P1Badge", "1P",
                new Vector2(lx, 420), new Vector2(100, 52), 36f, new Color(0.4f, 0.75f, 1f))
                .fontStyle = FontStyles.Bold;
            MakeOutline(_skillConfirmPanel.transform, "P1Line",
                new Vector2(lx, 378), new Vector2(300, 2), new Color(0.4f, 0.75f, 1f));

            _confirmP1Name = MakeLabel(_skillConfirmPanel.transform, "P1Name", "---",
                new Vector2(lx, 340), new Vector2(380, 38), 22f, new Color(1f, 1f, 1f));
            _confirmP1Name.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

            _confirmP1Desc = MakeLabel(_skillConfirmPanel.transform, "P1Desc", "",
                new Vector2(lx, 295), new Vector2(380, 38), 13f, new Color(0.82f, 0.88f, 1f));
            _confirmP1Desc.textWrappingMode = TMPro.TextWrappingModes.Normal;

            string[] slotLabels = { "基本技A", "基本技B", "基本技C", "スマッシュ" };
            float[] skillY      = { 220f, 130f, 40f, -60f };

            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                MakeLabel(_skillConfirmPanel.transform, $"P1SlotLabel{i}", slotLabels[i],
                    new Vector2(lx - 100f, skillY[i] + 20f), new Vector2(100f, 28f), 12f,
                    new Color(0.5f, 0.7f, 1f));
                _confirmP1SkillTexts[i] = MakeLabel(_skillConfirmPanel.transform, $"P1Skill{i}", "---",
                    new Vector2(lx + 30f, skillY[i]), new Vector2(340f, 54f), 14f, Color.white);
                _confirmP1SkillTexts[i].alignment = TextAlignmentOptions.TopLeft;
                _confirmP1SkillTexts[i].textWrappingMode = TMPro.TextWrappingModes.Normal;
            }

            // 2P 列（右）
            float rx = 440f;
            MakeLabel(_skillConfirmPanel.transform, "P2Badge", "2P",
                new Vector2(rx, 420), new Vector2(100, 52), 36f, new Color(1f, 0.55f, 0.35f))
                .fontStyle = FontStyles.Bold;
            MakeOutline(_skillConfirmPanel.transform, "P2Line",
                new Vector2(rx, 378), new Vector2(300, 2), new Color(1f, 0.55f, 0.35f));

            _confirmP2Name = MakeLabel(_skillConfirmPanel.transform, "P2Name", "---",
                new Vector2(rx, 340), new Vector2(380, 38), 22f, Color.white);
            _confirmP2Name.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

            _confirmP2Desc = MakeLabel(_skillConfirmPanel.transform, "P2Desc", "",
                new Vector2(rx, 295), new Vector2(380, 38), 13f, new Color(1f, 0.88f, 0.82f));
            _confirmP2Desc.textWrappingMode = TMPro.TextWrappingModes.Normal;

            for (int i = 0; i < 4; i++)
            {
                MakeLabel(_skillConfirmPanel.transform, $"P2SlotLabel{i}", slotLabels[i],
                    new Vector2(rx - 100f, skillY[i] + 20f), new Vector2(100f, 28f), 12f,
                    new Color(1f, 0.7f, 0.5f));
                _confirmP2SkillTexts[i] = MakeLabel(_skillConfirmPanel.transform, $"P2Skill{i}", "---",
                    new Vector2(rx + 30f, skillY[i]), new Vector2(340f, 54f), 14f, Color.white);
                _confirmP2SkillTexts[i].alignment = TextAlignmentOptions.TopLeft;
                _confirmP2SkillTexts[i].textWrappingMode = TMPro.TextWrappingModes.Normal;
            }

            // フッター
            var battleBtn = MakeButton(_skillConfirmPanel.transform, "BattleBtn", "バトル開始",
                new Vector2(0, -190), new Vector2(360, 64), OnSkillConfirmBattlePressed,
                new Color(0.1f, 0.55f, 0.1f, 1f));
            SetButtonLabelStyle(battleBtn, 26f, FontStyles.Bold, Color.white);

            MakeLabel(_skillConfirmPanel.transform, "BattleHint",
                "スペースキー: バトル開始　Esc: 戻る",
                new Vector2(0, -245), new Vector2(600, 28), 13f, new Color(0.72f, 0.8f, 1f));
        }

        void RefreshSkillConfirmContent()
        {
            void FillPlayer(CharacterData d, TextMeshProUGUI nameT, TextMeshProUGUI descT,
                TextMeshProUGUI[] skillTs)
            {
                if (d == null) return;
                if (nameT != null) nameT.text = d.characterName;
                if (descT != null) descT.text = d.visualDescription;
                for (int i = 0; i < 4 && i < skillTs.Length; i++)
                {
                    if (skillTs[i] == null) continue;
                    var s = d.skills[i];
                    skillTs[i].text = s != null ? $"{s.skill_name}\n{s.description}" : "---";
                }
            }

            FillPlayer(_pendingData1, _confirmP1Name, _confirmP1Desc, _confirmP1SkillTexts);
            FillPlayer(_pendingData2, _confirmP2Name, _confirmP2Desc, _confirmP2SkillTexts);
        }

        void ChangePreset(ref int idx, int delta, TextMeshProUGUI label)
        {
            if (_presets == null || _presets.Count == 0) return;
            idx = (idx + delta + _presets.Count) % _presets.Count;
            if (label != null) label.text = GetPresetName(idx);
            RefreshCharacterPreview();
        }

        void RefreshCharacterPreview()
        {
            SetPreview(_p1PreviewImage, _p1PresetIdx);
            SetPreview(_p2PreviewImage, _p2PresetIdx);
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

        string GetPresetName(int idx)
        {
            if (_presets == null || idx < 0 || idx >= _presets.Count) return "---";
            return _presets[idx].characterName;
        }

        void OnStartPressed()
        {
            if (BattleManager.Instance == null) return;
            if (_presets == null || _presets.Count == 0) return;

            bool hasP1Input = HasCharacterInput(true);
            bool hasP2Input = HasCharacterInput(false);

            // テキスト入力があれば AI 生成フローへ
            if (hasP1Input || hasP2Input)
            {
                var preset1 = GetPreset(true);
                var preset2 = GetPreset(false);
                _panel.SetActive(false);
                ShowGeneratingPanel();
                _generationCoroutine = StartCoroutine(GenerateBothChars(preset1, preset2, hasP1Input, hasP2Input));
                return;
            }

            // 入力なし: プリセットで即バトル（保存済みキャラの場合はスプライトセットをロード）
            var data1 = BuildCharacterData(true);
            var data2 = BuildCharacterData(false);
            EnsureSpriteSet(data1);
            EnsureSpriteSet(data2);
            _panel.SetActive(false);
            BattleManager.Instance.StartCountdown(data1, data2);
        }

        void OnSkillConfirmBattlePressed()
        {
            if (BattleManager.Instance == null || _pendingData1 == null || _pendingData2 == null) return;
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

            // 画像生成（base_visual_prompt が取得できていれば OpenAI Image API へ）
            UpdateGeneratingStatus("キャラクター画像を生成中...");
            yield return GenerateImages(_pendingData1, _pendingData2);

            _generatingPanel?.SetActive(false);
            _generationCoroutine = null;
            ShowSkillConfirmPanel();
        }

        IEnumerator GenerateImages(CharacterData data1, CharacterData data2)
        {
            bool img1Done = false, img2Done = false;

            if (data1 != null && !string.IsNullOrEmpty(data1.visualPrompt))
            {
                AIImageClient.GenerateSpriteSet(this, data1.visualPrompt,
                    msg => UpdateGeneratingStatus("1P " + msg),
                    sprites =>
                    {
                        data1.spriteSet = sprites;
                        data1.characterSprite = sprites.Get(CharacterSpriteId.Idle1);
                        img1Done = true;
                    },
                    err => { Debug.LogWarning("[AIImage] 1P: " + err); img1Done = true; },
                    saveDir: data1.spriteDir);
            }
            else img1Done = true;

            if (data2 != null && !string.IsNullOrEmpty(data2.visualPrompt))
            {
                AIImageClient.GenerateSpriteSet(this, data2.visualPrompt,
                    msg => UpdateGeneratingStatus("2P " + msg),
                    sprites =>
                    {
                        data2.spriteSet = sprites;
                        data2.characterSprite = sprites.Get(CharacterSpriteId.Idle1);
                        img2Done = true;
                    },
                    err => { Debug.LogWarning("[AIImage] 2P: " + err); img2Done = true; },
                    saveDir: data2.spriteDir);
            }
            else img2Done = true;

            yield return new WaitUntil(() => img1Done && img2Done);
        }

        // 保存済みスプライトがある場合はフルロードする（バトル開始直前に呼ぶ）
        static void EnsureSpriteSet(CharacterData data)
        {
            if (data == null) return;
            if (data.spriteSet?.Get(CharacterSpriteId.Idle1) != null) return;
            if (string.IsNullOrEmpty(data.spriteDir)) return;

            var loaded = CharacterSaveManager.LoadSpriteSet(data.spriteDir);
            if (loaded == null) return;

            data.spriteSet = loaded;
            if (data.characterSprite == null)
                data.characterSprite = loaded.Get(CharacterSpriteId.Idle1);
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
            var data1 = BuildCharacterData(true);
            var data2 = HasCharacterInput(false)
                ? BuildCharacterData(false)
                : PromptCharacterFactory.Clone(_presets[p2Idx]);

            _panel.SetActive(false);
            BattleManager.Instance.StartTraining(data1, data2);
        }

        void ShowPanel()
        {
            RefreshPresets();
            if (_trainingPanel != null) _trainingPanel.SetActive(false);
            if (_panel != null) _panel.SetActive(true);
            if (_titlePanel != null) _titlePanel.SetActive(false);
            _waitForMenuInputRelease = true;
        }

        // バトルから戻るたびにプリセットを再読み込みし、直前生成キャラを自動選択する
        void RefreshPresets()
        {
            string p1Name = GetPresetName(_p1PresetIdx);
            string p2Name = GetPresetName(_p2PresetIdx);

            _presets = new List<CharacterData>(PresetCharacterLoader.LoadAll());
            _presets.AddRange(CharacterSaveManager.LoadAll());

            int maxIdx = Mathf.Max(0, _presets.Count - 1);

            // 直前に選択していたキャラ名で再選択（保存済みキャラが増えてもインデックスがずれない）
            int f1 = p1Name != "---" ? _presets.FindIndex(c => c.characterName == p1Name) : -1;
            _p1PresetIdx = f1 >= 0 ? f1 : Mathf.Clamp(_p1PresetIdx, 0, maxIdx);

            int f2 = p2Name != "---" ? _presets.FindIndex(c => c.characterName == p2Name) : -1;
            _p2PresetIdx = f2 >= 0 ? f2 : Mathf.Clamp(_p2PresetIdx, 0, maxIdx);

            if (_p1PresetLabel != null) _p1PresetLabel.text = GetPresetName(_p1PresetIdx);
            if (_p2PresetLabel != null) _p2PresetLabel.text = GetPresetName(_p2PresetIdx);
            RefreshCharacterPreview();
        }

        void ShowTrainingPanel()
        {
            if (_trainingPanel != null) _trainingPanel.SetActive(true);
        }

        void ShowTitlePanel()
        {
            if (_titlePanel != null) _titlePanel.SetActive(true);
            if (_panel != null) _panel.SetActive(false);
            if (_trainingPanel != null) _trainingPanel.SetActive(false);
        }

        void ShowCharacterSelect()
        {
            if (_titlePanel != null) _titlePanel.SetActive(false);
            if (_panel != null) _panel.SetActive(true);
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
            if (_skillConfirmPanel != null) _skillConfirmPanel.SetActive(true);
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
            text.overflowMode = TextOverflowModes.Ellipsis;
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
            input.characterLimit = multiline ? 120 : 28;
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
            btn.onClick.AddListener(() => onClick?.Invoke());

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
