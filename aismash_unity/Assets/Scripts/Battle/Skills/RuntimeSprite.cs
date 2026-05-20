using UnityEngine;

namespace PromptFighters.Battle.Skills
{
    // ランタイム用の単色テクスチャ。Hitbox/Projectileの簡易表示に使う。
    public static class RuntimeSprite
    {
        static Sprite _square;
        static Sprite _circle;

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
    }
}
