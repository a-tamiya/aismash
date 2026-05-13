using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;
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

        GameObject _titlePanel;
        GameObject _panel;
        GameObject _trainingPanel;
        Image _titleTopGlow;
        Image _titleBottomGlow;
        RectTransform _titleMainRect;
        RectTransform _startButtonRect;

        void Awake()
        {
            EnsureInputSystemUIInputModule();
        }

        void Start()
        {
            _presets = PresetCharacterLoader.LoadAll();
            if (_presets.Count < 2) _p2PresetIdx = 0;
            BuildTitlePanel();
            BuildPanel();
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
                "1P: WASD + J/K/L/I    2P: 矢印 + テンキー1/2/3/5",
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
                "1P: WASD 移動 / J 近距離 / K 遠距離 / L 特殊 / I 必殺 / 左Shift ガード\n" +
                "2P: 矢印キー 移動 / テンキー1 近距離 / 2 遠距離 / 3 特殊 / 5 必殺 / 右Shift ガード",
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
                "1P: WASD 移動 / J K L I 技 / 左Shift ガード    2P: 矢印 移動 / テンキー1 2 3 5 技 / 右Shift ガード\n" +
                "Escキー: キャラ選択に戻る    Rキー: 位置・HP・クールダウンをリセット",
                new Vector2(0, 440), new Vector2(900, 52), 14, new Color(0.9f, 0.95f, 1f));
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
            string name = _presets[idx].characterName;
            return IsAscii(name) ? name : $"Preset {idx + 1}";
        }

        void OnStartPressed()
        {
            if (BattleManager.Instance == null) return;
            if (_presets == null || _presets.Count == 0) return;

            var data1 = CloneData(_presets[_p1PresetIdx]);
            var data2 = CloneData(_presets[_p2PresetIdx]);

            _panel.SetActive(false);
            BattleManager.Instance.StartCountdown(data1, data2);
        }

        void OnTrainingPressed()
        {
            if (BattleManager.Instance == null) return;
            if (_presets == null || _presets.Count == 0) return;

            int p2Idx = _presets.Count > 1 ? _p2PresetIdx : _p1PresetIdx;
            var data1 = CloneData(_presets[_p1PresetIdx]);
            var data2 = CloneData(_presets[p2Idx]);

            _panel.SetActive(false);
            BattleManager.Instance.StartTraining(data1, data2);
        }

        void ShowPanel()
        {
            if (_trainingPanel != null) _trainingPanel.SetActive(false);
            if (_panel != null) _panel.SetActive(true);
            if (_titlePanel != null) _titlePanel.SetActive(false);
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

        static CharacterData CloneData(CharacterData src)
        {
            return new CharacterData
            {
                characterName     = src.characterName,
                inputFeatures     = src.inputFeatures,
                visualPrompt      = src.visualPrompt,
                visualDescription = src.visualDescription,
                skills            = src.skills,
                spritePath        = src.spritePath,
                characterSprite   = src.characterSprite,
            };
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
