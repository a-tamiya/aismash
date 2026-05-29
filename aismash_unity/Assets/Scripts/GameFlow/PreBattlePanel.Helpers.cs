using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using TMPro;
using PromptFighters.UI;

namespace PromptFighters.GameFlow
{
    // PreBattlePanelの入力判定ヘルパーとuGUIビルダー（いずれもインスタンス状態に依存しないstaticユーティリティ）を
    // まとめた部分クラス。本体(PreBattlePanel.cs)のパネル構築ロジックと分離して見通しを良くするための分割。
    public partial class PreBattlePanel : MonoBehaviour
    {
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
            bg.color = UITheme.FieldBg;

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

        // アーケード調: 平行四辺形のメタリックバー（縦グラデ＋シアー）
        static Image MakeSlantBar(Transform parent, string name, Vector2 pos, Vector2 size, Color color, float slant)
        {
            var go = CreateUIObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = PromptFighters.UI.UITheme.VGradient;
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            PromptFighters.UI.UITheme.Skew(img, slant);
            return img;
        }

        // アーケード調: 既存ボタンをメタリック平行四辺形にスタイルする（ネオン縁付き）
        static void StyleArcadeButton(Button btn, Color baseColor, float slant)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = PromptFighters.UI.UITheme.VGradient;
                img.type = Image.Type.Simple;
                img.color = baseColor;
                PromptFighters.UI.UITheme.Skew(img, slant);
            }
            var rt = btn.GetComponent<RectTransform>();
            // 下辺ネオンライン
            var edge = MakeSlantBar(btn.transform, "BtnEdge",
                new Vector2(0f, -rt.sizeDelta.y * 0.5f + 2f),
                new Vector2(rt.sizeDelta.x, 4f),
                new Color(1f, 1f, 1f, 0.85f), slant);
            edge.transform.SetAsFirstSibling();
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
