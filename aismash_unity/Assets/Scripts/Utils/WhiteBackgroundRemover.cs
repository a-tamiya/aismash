using System.Collections.Generic;
using UnityEngine;

namespace PromptFighters.Utils
{
    // 白背景を透過処理する。
    // 外縁からのフラッドフィルに加えて、輪郭の内側に残りやすい純白背景も除去する。
    public static class WhiteBackgroundRemover
    {
        public static Texture2D ApplyChromaGreen(Texture2D src,
                                                 float greenThreshold = 0.68f,
                                                 float maxRedBlue = 0.38f,
                                                 float fadeRange = 0.16f)
        {
            int w = src.width;
            int h = src.height;
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.filterMode = src.filterMode;
            dst.wrapMode = src.wrapMode;

            Color[] pixels = src.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color p = pixels[i];
                float greenLead = p.g - Mathf.Max(p.r, p.b);
                bool strongGreen = p.g >= greenThreshold &&
                                   p.r <= maxRedBlue &&
                                   p.b <= maxRedBlue &&
                                   greenLead >= 0.22f;
                if (!strongGreen) continue;

                float t = Mathf.InverseLerp(0.22f, 0.22f + fadeRange, greenLead);
                p.a = 1f - Mathf.Clamp01(t);
                pixels[i] = p;
            }

            dst.SetPixels(pixels);
            dst.Apply();
            return dst;
        }

        // threshold : この値以上の min(R,G,B) を「白とみなせる」上限 (0-1)
        // fadeRange : エッジをグラデーションで馴染ませる幅
        public static Texture2D Apply(Texture2D src,
                                      float threshold = 0.97f,
                                      float fadeRange = 0.02f,
                                      bool removeInteriorWhite = true,
                                      float interiorThreshold = 0.94f,
                                      float maxColorSpread = 0.08f)
        {
            int w = src.width;
            int h = src.height;

            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.filterMode = src.filterMode;
            dst.wrapMode   = src.wrapMode;

            Color[] pixels = src.GetPixels();
            bool[]  isBg   = new bool[w * h]; // 背景フラグ

            float minThresh = threshold - fadeRange;
            var   queue     = new Queue<int>();

            // 外縁の白画素をシードとしてキューに積む
            for (int x = 0; x < w; x++)
            {
                TryEnqueue(x,     0,     w, pixels, isBg, queue, minThresh);
                TryEnqueue(x,     h - 1, w, pixels, isBg, queue, minThresh);
            }
            for (int y = 1; y < h - 1; y++)
            {
                TryEnqueue(0,     y, w, pixels, isBg, queue, minThresh);
                TryEnqueue(w - 1, y, w, pixels, isBg, queue, minThresh);
            }

            // 4方向フラッドフィル
            while (queue.Count > 0)
            {
                int i  = queue.Dequeue();
                int px = i % w;
                int py = i / w;
                if (px > 0)     TryEnqueue(px - 1, py,     w, pixels, isBg, queue, minThresh);
                if (px < w - 1) TryEnqueue(px + 1, py,     w, pixels, isBg, queue, minThresh);
                if (py > 0)     TryEnqueue(px,     py - 1, w, pixels, isBg, queue, minThresh);
                if (py < h - 1) TryEnqueue(px,     py + 1, w, pixels, isBg, queue, minThresh);
            }

            float interiorMinThresh = Mathf.Max(0f, interiorThreshold - fadeRange);

            // 背景と判定された画素、および内側に残った純白背景のアルファを下げる
            for (int i = 0; i < pixels.Length; i++)
            {
                Color p    = pixels[i];
                float minC = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
                bool shouldRemove = isBg[i];
                float fadeStart = minThresh;
                float fadeEnd = threshold;

                if (!shouldRemove && removeInteriorWhite && IsNeutralWhite(p, interiorMinThresh, maxColorSpread))
                {
                    shouldRemove = true;
                    fadeStart = interiorMinThresh;
                    fadeEnd = interiorThreshold;
                }

                if (!shouldRemove) continue;

                if (minC >= fadeEnd)
                {
                    p.a = 0f; // 完全透明
                }
                else
                {
                    // フェードゾーン
                    float t = (minC - fadeStart) / Mathf.Max(0.0001f, fadeEnd - fadeStart);
                    p.a = 1f - Mathf.Clamp01(t);
                }
                pixels[i] = p;
            }

            dst.SetPixels(pixels);
            dst.Apply();
            return dst;
        }

        static void TryEnqueue(int x, int y, int w,
                                Color[] pixels, bool[] isBg,
                                Queue<int> queue, float minThresh)
        {
            int idx = y * w + x;
            if (isBg[idx]) return;
            Color p    = pixels[idx];
            float minC = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
            if (minC < minThresh) return; // 白でない → 背景でない、ここで止める
            isBg[idx] = true;
            queue.Enqueue(idx);
        }

        static bool IsNeutralWhite(Color p, float minThreshold, float maxSpread)
        {
            float minC = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
            if (minC < minThreshold) return false;
            float maxC = Mathf.Max(p.r, Mathf.Max(p.g, p.b));
            return maxC - minC <= maxSpread;
        }
    }
}
