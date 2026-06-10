using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TextCore.LowLevel;

namespace PromptFighters.UI
{
    public static class UITheme
    {
        // ── アーケード格ゲー調パレット ──────────────────────────────
        // 暗いスチール地に、ネオン/ゴールドのアクセントを乗せる。
        public static readonly Color Steel      = new Color(0.06f, 0.07f, 0.10f, 0.98f); // フレーム地
        public static readonly Color SteelDark  = new Color(0.02f, 0.025f, 0.04f, 1.00f); // バー溝
        public static readonly Color SteelLight = new Color(0.20f, 0.23f, 0.30f, 1.00f); // ベベル明部
        public static readonly Color Gold       = new Color(1.00f, 0.78f, 0.18f, 1.00f); // 共通アクセント
        public static readonly Color GoldDim    = new Color(0.55f, 0.42f, 0.10f, 1.00f);
        public static readonly Color P1Neon     = new Color(0.20f, 0.70f, 1.00f, 1.00f); // 1P 青
        public static readonly Color P1NeonDark = new Color(0.05f, 0.22f, 0.45f, 1.00f);
        public static readonly Color P2Neon     = new Color(1.00f, 0.28f, 0.30f, 1.00f); // 2P 赤
        public static readonly Color P2NeonDark = new Color(0.45f, 0.06f, 0.10f, 1.00f);
        public static readonly Color FieldBg    = new Color(0.02f, 0.025f, 0.05f, 0.86f); // 入力欄の地
        public static readonly Color Ink        = Color.white;
        public static readonly Color InkDim     = new Color(0.62f, 0.70f, 0.84f, 1.00f);
        public static readonly Color Urgent     = new Color(1.00f, 0.24f, 0.16f, 1.00f);

        static TMP_FontAsset _font;
        static Sprite _solid;
        static Sprite _vgrad;

        public static TMP_FontAsset Font
        {
            get
            {
                if (_font != null) return _font;

                // Resources.Load はビルド後も動作する（File.Exists は動作しない）
                var unityFont = Resources.Load<UnityEngine.Font>("Fonts/keifont");
                if (unityFont != null)
                {
                    _font = TMP_FontAsset.CreateFontAsset(
                        unityFont, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048,
                        AtlasPopulationMode.Dynamic, true);
                    _font.name = "keifont Runtime SDF";
                    return _font;
                }

                // フォールバック: .exe の隣に font/keifont.ttf を置いた場合
                string fallback = Path.Combine(
                    Directory.GetParent(Application.dataPath).FullName, "font/keifont.ttf");
                if (File.Exists(fallback))
                {
                    _font = TMP_FontAsset.CreateFontAsset(
                        fallback, 0, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048);
                    _font.name = "keifont Runtime SDF";
                    _font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                    _font.isMultiAtlasTexturesEnabled = true;
                }

                return _font;
            }
        }

        public static void Apply(TMP_Text text, float? size = null, FontStyles? style = null)
        {
            if (text == null) return;
            if (Font != null) text.font = Font;
            if (size.HasValue) text.fontSize = size.Value;
            if (style.HasValue) text.fontStyle = style.Value;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.Normal;
        }

        public static void ApplyAllInScene()
        {
            foreach (var text in Object.FindObjectsByType<TMP_Text>())
                Apply(text);
        }

        // ── ランタイムスプライト ─────────────────────────────────────
        // 単色1pxスプライト。Image.color で着色して使う（角丸なしのフラット面）。
        public static Sprite Solid
        {
            get
            {
                if (_solid != null) return _solid;
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _solid = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
                _solid.name = "UITheme Solid";
                return _solid;
            }
        }

        // 縦方向グラデーション(下→上が明るくなる金属/エネルギー風)。Image.color で色相を載せる。
        public static Sprite VGradient
        {
            get
            {
                if (_vgrad != null) return _vgrad;
                const int h = 64;
                var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < h; y++)
                {
                    float t = y / (float)(h - 1);
                    // 下端を暗く、中央を明るく、上端をややハイライト（金属の照り）
                    float lum = Mathf.Lerp(0.55f, 1.15f, Mathf.SmoothStep(0f, 1f, t));
                    lum = Mathf.Min(lum, 1f) * (0.85f + 0.15f * Mathf.Sin(t * Mathf.PI));
                    tex.SetPixel(0, y, new Color(lum, lum, lum, 1f));
                }
                tex.Apply();
                _vgrad = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f), 100f);
                _vgrad.name = "UITheme VGradient";
                return _vgrad;
            }
        }

        // Image を生成して親に追加するショートカット。
        public static Image AddImage(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = sprite ?? Solid;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // Image に平行四辺形シアーを付与する。
        public static void Skew(Component target, float slantPixels)
        {
            var s = target.GetComponent<UISkew>() ?? target.gameObject.AddComponent<UISkew>();
            s.slantPixels = slantPixels;
        }
    }
}
