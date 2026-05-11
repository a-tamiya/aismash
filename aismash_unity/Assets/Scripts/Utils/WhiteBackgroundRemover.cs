using UnityEngine;

namespace PromptFighters.Utils
{
    // テクスチャの白背景を透過処理する。
    // DALL·E等で生成した白背景立ち絵にも使えるよう、Phase 4でも流用する。
    public static class WhiteBackgroundRemover
    {
        // threshold: この値以上のmin(R,G,B)を白とみなす (0-1)
        // fadeRange: エッジを滑らかにするグラデーション幅
        public static Texture2D Apply(Texture2D src,
                                      float threshold = 0.88f,
                                      float fadeRange = 0.10f)
        {
            // RGBA32形式で新テクスチャを作成（ミップマップなし）
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            dst.filterMode = src.filterMode;
            dst.wrapMode   = src.wrapMode;

            Color[] pixels = src.GetPixels();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color p = pixels[i];
                // min(R,G,B): 3チャンネルが全て高いときだけ「白」と判定
                // → キャラのオレンジ・黒など彩度ある色は誤除去しない
                float minC = Mathf.Min(p.r, Mathf.Min(p.g, p.b));

                if (minC >= threshold)
                {
                    // 完全透明
                    p.a = 0f;
                }
                else if (minC >= threshold - fadeRange)
                {
                    // エッジをグラデーション
                    float t = (minC - (threshold - fadeRange)) / fadeRange;
                    p.a = 1f - t;
                }
                // else: alpha維持（元が不透明なら1のまま）

                pixels[i] = p;
            }

            dst.SetPixels(pixels);
            dst.Apply();
            return dst;
        }
    }
}
