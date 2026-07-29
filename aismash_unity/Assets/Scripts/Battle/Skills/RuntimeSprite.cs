using UnityEngine;

namespace PromptFighters.Battle.Skills
{
    // ランタイム用の単色テクスチャ。Hitbox/Projectileの簡易表示に使う。
    public static class RuntimeSprite
    {
        static Sprite _square;
        static Sprite _circle;
        static Sprite _glow;
        static Sprite _downArrow;
        static Sprite _telegraphRadial;
        static Sprite _telegraphLine;
        static Sprite _telegraphBox;
        static Sprite _fallbackProjectile;
        static Sprite _fallbackImpact;
        static Sprite _fallbackSummon;
        static Sprite _fallbackWall;
        static Sprite _fallbackField;

        static Sprite LoadGenerated(ref Sprite cache, string resourcePath)
        {
            if (cache == null) cache = Resources.Load<Sprite>(resourcePath);
            return cache;
        }

        // GPTで生成した、実戦表示用の高品質フォールバック素材。
        // Resourcesが欠けた開発環境でもゲーム進行を止めないよう、最後だけ従来の図形へ戻す。
        public static Sprite TelegraphRadial()
            => LoadGenerated(ref _telegraphRadial, "Effects/Fallback/telegraph_radial") ?? Glow();

        public static Sprite TelegraphLine()
            => LoadGenerated(ref _telegraphLine, "Effects/Fallback/telegraph_line") ?? Square();

        public static Sprite TelegraphBox()
            => LoadGenerated(ref _telegraphBox, "Effects/Fallback/telegraph_box") ?? Square();

        public static Sprite FallbackProjectile()
            => LoadGenerated(ref _fallbackProjectile, "Effects/Fallback/fallback_projectile") ?? Glow();

        public static Sprite FallbackImpact()
            => LoadGenerated(ref _fallbackImpact, "Effects/Fallback/fallback_impact") ?? Glow();

        public static Sprite FallbackSummon()
            => LoadGenerated(ref _fallbackSummon, "Effects/Fallback/fallback_summon") ?? Glow();

        public static Sprite FallbackWall()
            => LoadGenerated(ref _fallbackWall, "Effects/Fallback/fallback_wall") ?? Square();

        public static Sprite FallbackField()
            => LoadGenerated(ref _fallbackField, "Effects/Fallback/fallback_field") ?? Circle();

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

        // 下向き矢印（▽）。ダウン中の味方など、頭上マーカーの目印に使う。
        public static Sprite DownArrow()
        {
            if (_downArrow != null) return _downArrow;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = x / (float)(size - 1);
                    float fy = y / (float)(size - 1); // 0=下端, 1=上端
                    float halfWidthAllow = fy * 0.5f;  // 上端=全幅、下端=先端の点
                    float dx = Mathf.Abs(fx - 0.5f);
                    pixels[y * size + x] = dx <= halfWidthAllow ? Color.white : Color.clear;
                }
            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            _downArrow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return _downArrow;
        }
    }
}
