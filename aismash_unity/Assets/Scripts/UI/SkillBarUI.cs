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
        Image[]         _cooldownFill;
        TextMeshProUGUI[] _skillNames;
        TextMeshProUGUI[] _keybindLabels;

        static readonly string[] Keys1P = { "J", "K", "L", "I" };
        static readonly string[] Keys2P = { "Num1", "Num2", "Num3", "Num5" };
        static readonly string[] SlotLabels = { "近距離", "遠距離", "特殊", "必殺" };
        static readonly Color CooldownColor = new Color(0f, 0f, 0f, 0.7f);
        static readonly Color ReadyColor    = new Color(0.2f, 0.8f, 0.3f, 0.5f);

        void Start()
        {
            BuildUI();
        }

        void BuildUI()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            _slots         = new RectTransform[4];
            _cooldownFill  = new Image[4];
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

                // クールダウンオーバーレイ（上から下に縮む）
                var fill = CreateRect("CoolFill", slot);
                fill.anchorMin = new Vector2(0, 0);
                fill.anchorMax = new Vector2(1, 0);
                fill.pivot     = new Vector2(0.5f, 0f);
                fill.sizeDelta = Vector2.zero;
                var fillImg = AddImage(fill.gameObject, CooldownColor);
                _cooldownFill[i] = fillImg;

                // 技名
                var nameObj = CreateRect("SkillName", slot);
                nameObj.anchoredPosition = new Vector2(0, 12f);
                nameObj.sizeDelta        = new Vector2(90f, 22f);
                var nameTmp = nameObj.gameObject.AddComponent<TextMeshProUGUI>();
                nameTmp.fontSize          = 10;
                nameTmp.alignment         = TextAlignmentOptions.Center;
                nameTmp.color             = Color.white;
                nameTmp.enableWordWrapping = false;
                nameTmp.overflowMode      = TextOverflowModes.Ellipsis;
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

                // キーバインド
                var keyObj = CreateRect("Key", slot);
                keyObj.anchoredPosition = new Vector2(0, -26f);
                keyObj.sizeDelta        = new Vector2(90f, 16f);
                var keyTmp = keyObj.gameObject.AddComponent<TextMeshProUGUI>();
                keyTmp.fontSize  = 8;
                keyTmp.alignment = TextAlignmentOptions.Center;
                keyTmp.color     = new Color(1f, 0.9f, 0.5f);
                keyTmp.text      = keys[i];
                _keybindLabels[i] = keyTmp;
            }

            RefreshNames();
        }

        void Update()
        {
            if (skillExecutor == null) return;
            for (int i = 0; i < 4; i++)
            {
                if (_cooldownFill[i] == null) continue;
                var slot = (SkillSlot)i;
                var skill = skillExecutor.GetSkill(slot);
                if (skill == null) continue;

                float cd    = skillExecutor.GetCooldown(slot);
                float maxCD = skill.parameters.cooldown;
                float t     = maxCD > 0f ? Mathf.Clamp01(cd / maxCD) : 0f;

                // fillのheightをtに合わせてクールダウンをオーバーレイ
                var rt = _cooldownFill[i].rectTransform;
                rt.offsetMax = new Vector2(0f, _slots[i].sizeDelta.y * t);
                rt.offsetMin = Vector2.zero;

                // 背景色: 準備完了なら緑ぎみ
                AddImage(_slots[i].gameObject, t <= 0f
                    ? new Color(0.1f, 0.25f, 0.12f, 0.85f)
                    : new Color(0.1f, 0.1f, 0.15f, 0.85f));
            }
        }

        void RefreshNames()
        {
            if (skillExecutor == null) return;
            for (int i = 0; i < 4; i++)
            {
                if (_skillNames[i] == null) continue;
                var s = skillExecutor.GetSkill((SkillSlot)i);
                _skillNames[i].text = s != null ? s.skill_name : "---";
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
    }
}
