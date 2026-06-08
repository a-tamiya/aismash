using UnityEngine;

namespace PromptFighters.Battle.Skills
{
    // ランタイム用の単色テクスチャ。Hitbox/Projectileの簡易表示に使う。
    public static class RuntimeSprite
    {
        static Sprite _square;
        static Sprite _circle;
        static Sprite _glow;

        public static Sprite Square()
        {
            if (_square != null) return _square;
            var tex = new Texture2D(2, 2);
            var px = new Color[] { Color.white, Color.white, Color.white, Color.white };
            tex.SetPixels(px);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            _square = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2);
            return _square;
        }

        public static Sprite Circle()
        {
            if (_circle != null) return _circle;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size * 0.5f;
            float r2 = (center - 0.5f) * (center - 0.5f);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f, dy = y - center + 0.5f;
                    pixels[y * size + x] = (dx * dx + dy * dy <= r2) ? Color.white : Color.clear;
                }
            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return _circle;
        }

        // 画像生成に失敗した技エフェクト用のフォールバック。四角ではなく、
        // 中心が明るく外周へ柔らかく減衰するエネルギー塊（放射グラデーション）。
        public static Sprite Glow()
        {
            if (_glow != null) return _glow;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size * 0.5f;
            float maxR = center - 0.5f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f, dy = y - center + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / maxR; // 0=中心, 1=外周
                    // 中心は白く飽和、外周はフェード。やわらかなコア＋ハロー。
                    float a    = Mathf.Clamp01(1f - d);
                    a          = a * a;                       // 外周をより急に減衰
                    float core = Mathf.Clamp01(1f - d * 2.2f); // 中心の白いコア
                    pixels[y * size + x] = new Color(
                        Mathf.Clamp01(0.6f + core),
                        Mathf.Clamp01(0.6f + core),
                        Mathf.Clamp01(0.6f + core),
                        a);
                }
            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            _glow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return _glow;
        }
    }
}
