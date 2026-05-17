using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace PromptFighters.UI
{
    public static class UITheme
    {
        static TMP_FontAsset _font;

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
            foreach (var text in Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
                Apply(text);
        }
    }
}
