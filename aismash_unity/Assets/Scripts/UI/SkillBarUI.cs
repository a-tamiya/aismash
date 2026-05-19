using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptFighters.Battle.Skills;

namespace PromptFighters.UI
{
    // 1ファイター分の4スロット技バーをランタイムで生成する。
    // Canvas子として配置し、SkillExecutorを参照するだけで動く。
    public class SkillBarUI : MonoBehaviour
    {
        [Header("References")]
        public SkillExecutor skillExecutor;

        [Header("Layout")]
        public bool isLeftSide = true;  // 1P=左, 2P=右

        RectTransform[] _slots;
        TextMeshProUGUI[] _skillNames;
        TextMeshProUGUI[] _keybindLabels;

        static readonly string[] Keys1P = { "J", "K", "L", "A/D+J" };
        static readonly string[] Keys2P = { "Num2", "Num3", "Num1", "←/→+Num2" };
        static readonly string[] SlotLabels = { "Attack A", "Attack B", "Attack C", "Smash" };

        void Start() { }

        void BuildUI()
        {
            return; // 下部スキルバーは非表示
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            _slots         = new RectTransform[4];
            _skillNames    = new TextMeshProUGUI[4];
            _keybindLabels = new TextMeshProUGUI[4];

            string[] keys = isLeftSide ? Keys1P : Keys2P;
            float xStart  = isLeftSide ? -600f : 200f;

            for (int i = 0; i < 4; i++)
            {
                float x = xStart + i * 105f;

                // スロット背景
                var slot = CreateRect($"Slot_{i}", transform);
                slot.anchoredPosition = new Vector2(x, -340f);
                slot.sizeDelta        = new Vector2(95f, 70f);
                slot.anchorMin = slot.anchorMax = new Vector2(0.5f, 0.5f);
                AddImage(slot.gameObject, new Color(0.1f, 0.1f, 0.15f, 0.85f));
                _slots[i] = slot;

                // 技名
                var nameObj = CreateRect("SkillName", slot);
                nameObj.anchoredPosition = new Vector2(0, 12f);
                nameObj.sizeDelta        = new Vector2(90f, 22f);
                var nameTmp = nameObj.gameObject.AddComponent<TextMeshProUGUI>();
                nameTmp.fontSize          = 10;
                nameTmp.alignment         = TextAlignmentOptions.Center;
                nameTmp.color             = Color.white;
                nameTmp.textWrappingMode = TextWrappingModes.NoWrap;
                nameTmp.overflowMode      = TextOverflowModes.Ellipsis;
                UITheme.Apply(nameTmp);
                _skillNames[i] = nameTmp;

                // スロットラベル
                var labelObj = CreateRect("SlotLabel", slot);
                labelObj.anchoredPosition = new Vector2(0, -8f);
                labelObj.sizeDelta        = new Vector2(90f, 18f);
                var labelTmp = labelObj.gameObject.AddComponent<TextMeshProUGUI>();
                labelTmp.fontSize  = 9;
                labelTmp.alignment = TextAlignmentOptions.Center;
                labelTmp.color     = new Color(0.8f, 0.8f, 0.8f);
                labelTmp.text      = SlotLabels[i];
                UITheme.Apply(labelTmp);

                // キーバインド
                var keyObj = CreateRect("Key", slot);
                keyObj.anchoredPosition = new Vector2(0, -26f);
                keyObj.sizeDelta        = new Vector2(90f, 16f);
                var keyTmp = keyObj.gameObject.AddComponent<TextMeshProUGUI>();
                keyTmp.fontSize  = 8;
                keyTmp.alignment = TextAlignmentOptions.Center;
                keyTmp.color     = new Color(1f, 0.9f, 0.5f);
                keyTmp.text      = keys[i];
                UITheme.Apply(keyTmp);
                _keybindLabels[i] = keyTmp;
            }

            RefreshNames();
        }

        void Update()
        {
        }

        void RefreshNames()
        {
            if (skillExecutor == null) return;
            for (int i = 0; i < 4; i++)
            {
                if (_skillNames[i] == null) continue;
                var s = skillExecutor.GetSkill((SkillSlot)i);
                _skillNames[i].text = s != null && IsAscii(s.skill_name) ? s.skill_name : SlotLabels[i];
            }
        }

        public void OnCharacterLoaded() => RefreshNames();

        // ── ヘルパー ──────────────────────────────────────────────

        static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        static Image AddImage(GameObject go, Color color)
        {
            var img = go.GetComponent<Image>();
            if (img == null) img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        static bool IsAscii(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            for (int i = 0; i < value.Length; i++)
                if (value[i] > 127) return false;
            return true;
        }
    }
}
