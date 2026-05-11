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

                string path = Path.Combine(Application.dataPath, "Resources/Fonts/keifont.ttf");
                if (!File.Exists(path))
                    path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "font/keifont.ttf");

                if (File.Exists(path))
                {
                    _font = TMP_FontAsset.CreateFontAsset(
                        path, 0, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048);
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
