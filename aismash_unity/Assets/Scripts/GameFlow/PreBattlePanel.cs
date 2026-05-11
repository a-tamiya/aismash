using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;

namespace PromptFighters.GameFlow
{
    public class PreBattlePanel : MonoBehaviour
    {
        List<CharacterData> _presets;
        int _p1PresetIdx = 0;
        int _p2PresetIdx = 1;

        TextMeshProUGUI _p1PresetLabel;
        TextMeshProUGUI _p2PresetLabel;

        GameObject _titlePanel;
        GameObject _panel;
        GameObject _trainingPanel;
        Image _titleTopGlow;
        Image _titleBottomGlow;
        RectTransform _titleMainRect;
        RectTransform _startButtonRect;

        void Start()
        {
            _presets = PresetCharacterLoader.LoadAll();
            if (_presets.Count < 2) _p2PresetIdx = 0;
            BuildTitlePanel();
            BuildPanel();
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

            MakeLabel(_titlePanel.transform, "TitleKicker", "AI PROMPT FIGHTER",
                new Vector2(0, 120), new Vector2(620, 44), 22, new Color(0.75f, 0.95f, 1f));
            var title = MakeLabel(_titlePanel.transform, "TitleMain", "PROMPT FIGHTERS",
                new Vector2(0, 54), new Vector2(760, 92), 58, new Color(1f, 0.92f, 0.46f));
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 2f;
            _titleMainRect = title.rectTransform;

            MakeLabel(_titlePanel.transform, "TitleSub",
                "言葉から生まれるキャラクターで戦う 2D 対戦アクション",
                new Vector2(0, -12), new Vector2(700, 38), 18, new Color(0.92f, 0.96f, 1f));
            MakeLabel(_titlePanel.transform, "ApiNote",
                "API準備中でもテストキャラとトレーニングで操作確認できます",
                new Vector2(0, -58), new Vector2(700, 28), 13, new Color(0.72f, 0.84f, 1f));

            var startButton = MakeButton(_titlePanel.transform, "GameStartBtn", "GAME START",
                new Vector2(0, -128), new Vector2(310, 62), ShowCharacterSelect,
                new Color(0.92f, 0.42f, 0.08f, 1f));
            SetButtonLabelStyle(startButton, 22f, FontStyles.Bold, new Color(1f, 0.98f, 0.9f));
            _startButtonRect = startButton.GetComponent<RectTransform>();
            MakeLabel(_titlePanel.transform, "StartHelp", "Space / Enter",
                new Vector2(0, -178), new Vector2(240, 24), 12, new Color(0.8f, 0.88f, 1f));

            MakeLabel(_titlePanel.transform, "Footer",
                "1P: WASD + J/K/L/I    2P: Arrows + Num1/2/3/5",
                new Vector2(0, -286), new Vector2(760, 28), 12, new Color(0.76f, 0.82f, 0.9f));
        }

        void BuildPanel()
        {
            _panel = CreateUIObject("PreBattleOverlay", transform);
            StretchFull(_panel.GetComponent<RectTransform>());

            var bg = _panel.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.1f, 0.95f);

            // CanvasGroupでレイキャスト対象に
            var cg = _panel.AddComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;

            // タイトル
            MakeLabel(_panel.transform, "タイトル", "Prompt Fighters",
                new Vector2(0, 180), new Vector2(600, 70), 40, Color.yellow);
            MakeLabel(_panel.transform, "Sub", "キャラクターを選択してください",
                new Vector2(0, 120), new Vector2(500, 40), 20, Color.white);

            // 1P
            MakeLabel(_panel.transform, "P1Lbl", "1P",
                new Vector2(-250, 60), new Vector2(220, 40), 24, new Color(0.5f, 0.8f, 1f));

            var p1Row = CreateUIObject("P1Row", _panel.transform);
            p1Row.GetComponent<RectTransform>().anchoredPosition = new Vector2(-250, 10);
            p1Row.GetComponent<RectTransform>().sizeDelta = new Vector2(280, 40);
            p1Row.GetComponent<RectTransform>().anchorMin = p1Row.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);

            MakeButton(p1Row.transform, "P1L", "◀", new Vector2(-120, 0), new Vector2(40, 36),
                () => ChangePreset(ref _p1PresetIdx, -1, _p1PresetLabel));
            _p1PresetLabel = MakeLabel(p1Row.transform, "P1Preset", GetPresetName(_p1PresetIdx),
                new Vector2(0, 0), new Vector2(190, 36), 14, Color.white);
            MakeButton(p1Row.transform, "P1R", "▶", new Vector2(120, 0), new Vector2(40, 36),
                () => ChangePreset(ref _p1PresetIdx, +1, _p1PresetLabel));

            // 2P
            MakeLabel(_panel.transform, "P2Lbl", "2P",
                new Vector2(250, 60), new Vector2(220, 40), 24, new Color(1f, 0.6f, 0.4f));

            var p2Row = CreateUIObject("P2Row", _panel.transform);
            p2Row.GetComponent<RectTransform>().anchoredPosition = new Vector2(250, 10);
            p2Row.GetComponent<RectTransform>().sizeDelta = new Vector2(280, 40);
            p2Row.GetComponent<RectTransform>().anchorMin = p2Row.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);

            MakeButton(p2Row.transform, "P2L", "◀", new Vector2(-120, 0), new Vector2(40, 36),
                () => ChangePreset(ref _p2PresetIdx, -1, _p2PresetLabel));
            _p2PresetLabel = MakeLabel(p2Row.transform, "P2Preset", GetPresetName(_p2PresetIdx),
                new Vector2(0, 0), new Vector2(190, 36), 14, Color.white);
            MakeButton(p2Row.transform, "P2R", "▶", new Vector2(120, 0), new Vector2(40, 36),
                () => ChangePreset(ref _p2PresetIdx, +1, _p2PresetLabel));

            // 操作説明
            MakeLabel(_panel.transform, "CtrlHelp",
                "1P: WASD移動 / J近距離 / K遠距離 / L特殊 / I必殺 / LShiftガード\n" +
                "2P: 矢印移動 / Num1近距離 / Num2遠距離 / Num3特殊 / Num5必殺 / RShiftガード",
                new Vector2(0, -70), new Vector2(700, 60), 12, new Color(0.8f, 0.8f, 0.8f));

            // 開始ボタン
            MakeButton(_panel.transform, "StartBtn", "バトル開始！  (Space)",
                new Vector2(-150, -150), new Vector2(260, 56), OnStartPressed,
                new Color(0.15f, 0.6f, 0.15f));

            MakeButton(_panel.transform, "TrainingBtn", "トレーニング  (T)",
                new Vector2(150, -150), new Vector2(260, 56), OnTrainingPressed,
                new Color(0.15f, 0.35f, 0.65f));

            MakeLabel(_panel.transform, "TrainingHelp",
                "API画像生成待ち中は、テストキャラで操作確認できます。",
                new Vector2(0, -205), new Vector2(700, 30), 13, new Color(0.8f, 0.9f, 1f));

            BuildTrainingPanel();
        }

        void BuildTrainingPanel()
        {
            _trainingPanel = CreateUIObject("TrainingOverlay", transform);
            StretchFull(_trainingPanel.GetComponent<RectTransform>());
            _trainingPanel.SetActive(false);

            var cg = _trainingPanel.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = false;

            MakeLabel(_trainingPanel.transform, "TrainingTitle", "TRAINING",
                new Vector2(0, 285), new Vector2(280, 46), 28, new Color(0.5f, 0.85f, 1f));
            MakeLabel(_trainingPanel.transform, "TrainingControls",
                "1P: WASD / J K L I / LShift    2P: Arrows / Num1 Num2 Num3 Num5 / RShift\n" +
                "Esc: キャラ選択へ戻る    R: 位置とHPをリセット",
                new Vector2(0, 235), new Vector2(820, 48), 13, new Color(0.9f, 0.95f, 1f));
        }

        void ChangePreset(ref int idx, int delta, TextMeshProUGUI label)
        {
            if (_presets == null || _presets.Count == 0) return;
            idx = (idx + delta + _presets.Count) % _presets.Count;
            if (label != null) label.text = GetPresetName(idx);
        }

        string GetPresetName(int idx) =>
            (_presets != null && idx < _presets.Count) ? _presets[idx].characterName : "---";

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
            };
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
