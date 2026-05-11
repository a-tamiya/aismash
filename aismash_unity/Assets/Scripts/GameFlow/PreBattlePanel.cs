using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;

namespace PromptFighters.GameFlow
{
    // バトル前のセットアップパネル（名前入力 + プリセット選択）。
    // CanvasにアタッチするだけでUI全体をランタイムに生成する。
    public class PreBattlePanel : MonoBehaviour
    {
        List<CharacterData> _presets;
        int _p1PresetIdx = 0;
        int _p2PresetIdx = 1;

        TMP_InputField _p1NameInput;
        TMP_InputField _p2NameInput;
        TextMeshProUGUI _p1PresetLabel;
        TextMeshProUGUI _p2PresetLabel;

        GameObject _panel;

        void Start()
        {
            _presets = PresetCharacterLoader.LoadAll();
            if (_presets.Count < 2) _p2PresetIdx = 0;

            BuildPanel();

            if (BattleManager.Instance != null)
                BattleManager.Instance.OnReturnedToSetup += ShowPanel;
        }

        void BuildPanel()
        {
            // 半透明オーバーレイ
            _panel = new GameObject("PreBattlePanel");
            _panel.transform.SetParent(transform, false);

            var bg = _panel.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.1f, 0.93f);
            var bgRt = _panel.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

            // タイトル
            AddLabel(_panel.transform, "タイトル", "プロンプトファイターズ",
                new Vector2(0, 140), new Vector2(500, 60), 36, Color.yellow);
            AddLabel(_panel.transform, "Sub", "キャラクターを選んでください",
                new Vector2(0, 90), new Vector2(450, 36), 18, Color.white);

            // 1P 列
            AddLabel(_panel.transform, "P1Label", "1P",
                new Vector2(-230, 40), new Vector2(200, 36), 22, new Color(0.4f, 0.7f, 1f));
            _p1NameInput  = AddInputField(_panel.transform, "P1Name",
                new Vector2(-230, 0), new Vector2(200, 36), "キャラクター名");

            var p1Left  = AddButton(_panel.transform, "P1L", "◀",
                new Vector2(-330, -45), new Vector2(40, 36), () => ChangePreset(ref _p1PresetIdx, -1, _p1PresetLabel));
            _p1PresetLabel = AddLabel(_panel.transform, "P1Preset", GetPresetName(_p1PresetIdx),
                new Vector2(-230, -45), new Vector2(180, 36), 13, Color.white);
            var p1Right = AddButton(_panel.transform, "P1R", "▶",
                new Vector2(-130, -45), new Vector2(40, 36), () => ChangePreset(ref _p1PresetIdx, +1, _p1PresetLabel));

            // 2P 列
            AddLabel(_panel.transform, "P2Label", "2P",
                new Vector2(230, 40), new Vector2(200, 36), 22, new Color(1f, 0.6f, 0.4f));
            _p2NameInput  = AddInputField(_panel.transform, "P2Name",
                new Vector2(230, 0), new Vector2(200, 36), "キャラクター名");

            var p2Left  = AddButton(_panel.transform, "P2L", "◀",
                new Vector2(130, -45), new Vector2(40, 36), () => ChangePreset(ref _p2PresetIdx, -1, _p2PresetLabel));
            _p2PresetLabel = AddLabel(_panel.transform, "P2Preset", GetPresetName(_p2PresetIdx),
                new Vector2(230, -45), new Vector2(180, 36), 13, Color.white);
            var p2Right = AddButton(_panel.transform, "P2R", "▶",
                new Vector2(330, -45), new Vector2(40, 36), () => ChangePreset(ref _p2PresetIdx, +1, _p2PresetLabel));

            // 操作説明
            AddLabel(_panel.transform, "CtrlHelp",
                "1P: WASD移動 / J近 / K遠 / L特 / I必 / LShiftガード\n" +
                "2P: 矢印移動 / Num1近 / Num2遠 / Num3特 / Num5必 / RShiftガード",
                new Vector2(0, -105), new Vector2(600, 48), 11, new Color(0.8f, 0.8f, 0.8f));

            // バトル開始ボタン
            AddButton(_panel.transform, "StartBtn", "バトル開始！",
                new Vector2(0, -160), new Vector2(200, 50), OnStartPressed,
                new Color(0.2f, 0.7f, 0.2f));
        }

        void ChangePreset(ref int idx, int delta, TextMeshProUGUI label)
        {
            idx = (idx + delta + _presets.Count) % _presets.Count;
            label.text = GetPresetName(idx);
        }

        string GetPresetName(int idx) =>
            (_presets != null && idx < _presets.Count) ? _presets[idx].characterName : "---";

        void OnStartPressed()
        {
            if (BattleManager.Instance == null) return;

            var data1 = CloneWithName(_presets[_p1PresetIdx], _p1NameInput.text);
            var data2 = CloneWithName(_presets[_p2PresetIdx], _p2NameInput.text);

            _panel.SetActive(false);
            BattleManager.Instance.StartCountdown(data1, data2);
        }

        void ShowPanel()
        {
            if (_panel != null) _panel.SetActive(true);
        }

        static CharacterData CloneWithName(CharacterData src, string nameOverride)
        {
            string name = string.IsNullOrWhiteSpace(nameOverride) ? src.characterName : nameOverride.Trim();
            var clone = new CharacterData
            {
                characterName     = name,
                inputFeatures     = src.inputFeatures,
                visualPrompt      = src.visualPrompt,
                visualDescription = src.visualDescription,
                skills            = src.skills,
            };
            return clone;
        }

        // ── UIヘルパー ────────────────────────────────────────────

        static TextMeshProUGUI AddLabel(Transform parent, string goName, string text,
            Vector2 pos, Vector2 size, float fontSize, Color color)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = fontSize;
            tmp.color     = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            return tmp;
        }

        static TMP_InputField AddInputField(Transform parent, string goName,
            Vector2 pos, Vector2 size, string placeholder)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.2f, 1f);

            var field = go.AddComponent<TMP_InputField>();

            // テキスト表示用
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(6, 3);
            textRt.offsetMax = new Vector2(-6, -3);
            var textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.fontSize  = 14;
            textTmp.color     = Color.white;
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;
            field.textComponent = textTmp;

            // プレースホルダー
            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(go.transform, false);
            var phRt = phGo.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(6, 3);
            phRt.offsetMax = new Vector2(-6, -3);
            var phTmp = phGo.AddComponent<TextMeshProUGUI>();
            phTmp.text      = placeholder;
            phTmp.fontSize  = 14;
            phTmp.color     = new Color(0.5f, 0.5f, 0.5f);
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;
            phTmp.fontStyle = FontStyles.Italic;
            field.placeholder = phTmp;

            return field;
        }

        static Button AddButton(Transform parent, string goName, string label,
            Vector2 pos, Vector2 size, System.Action onClick, Color? bgColor = null)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = bgColor ?? new Color(0.2f, 0.2f, 0.3f, 1f);

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.35f, 0.35f, 0.5f);
            colors.pressedColor     = new Color(0.1f, 0.1f, 0.15f);
            btn.colors = colors;
            btn.onClick.AddListener(() => onClick());

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = textRt.offsetMax = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.fontSize  = 15;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            return btn;
        }
    }
}
