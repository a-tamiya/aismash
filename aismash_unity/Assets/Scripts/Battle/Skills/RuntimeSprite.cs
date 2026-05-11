using UnityEngine;

namespace PromptFighters.Battle.Skills
{
    // ランタイム用の単色テクスチャ。Hitbox/Projectileの簡易表示に使う。
    public static class RuntimeSprite
    {
        static Sprite _square;

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
    }
}
