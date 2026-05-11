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

        GameObject _panel;

        void Start()
        {
            _presets = PresetCharacterLoader.LoadAll();
            if (_presets.Count < 2) _p2PresetIdx = 0;
            BuildPanel();

            if (BattleManager.Instance != null)
                BattleManager.Instance.OnReturnedToSetup += ShowPanel;
        }

        void Update()
        {
            // Spaceキーでもバトル開始できるようにする
            if (_panel != null && _panel.activeSelf)
            {
                if (UnityEngine.InputSystem.Keyboard.current != null &&
                    UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    OnStartPressed();
                }
            }
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
                new Vector2(0, -140), new Vector2(260, 56), OnStartPressed,
                new Color(0.15f, 0.6f, 0.15f));
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

        void ShowPanel()
        {
            if (_panel != null) _panel.SetActive(true);
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
            tmp.enableWordWrapping = true;
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
    }
}
