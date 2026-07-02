using System.Collections.Generic;
using UnityEngine;

namespace PromptFighters.Battle.Skills
{
    // 攻撃ポーズのスプライトをピクセル解析し、「武器の先端／銃口／拳」の位置（攻撃アンカー）を推定する。
    // AI生成スプライトは武器やポーズが毎回違うため、固定オフセットだけでは
    // 「銃口とずれた位置から弾が出る」「剣を振っているのに刃の位置に判定がない」が起きる。
    // 前方（+X）へ突き出た細い部分＝得物（武器）や拳とみなし、発射位置・判定をそこへ合わせる。
    //
    // 座標系: キャラのピボット（足元中央）基準・キャラ標準サイズのローカル単位。
    // スプライトは pivot=(0.5,0)・PPU=高さ/2 で生成されるため、画像の縦幅＝2.0ユニットに相当する。
    public struct AttackAnchor
    {
        public bool    valid;
        public Vector2 tip;          // 前方突出部の先端（武器の先・銃口・拳）
        public float   bodyEdgeX;    // 胴体シルエットの前端X（ここから先が得物）
        public float   weaponLength; // tip.x - bodyEdgeX（一定以上で「得物あり」とみなす）
    }

    public static class AttackAnchorEstimator
    {
        const float AlphaThreshold255 = 64f; // これ以上の不透明度(0-255)をシルエットとみなす
        const int   TargetSamples     = 220; // 解析の目標解像度（縦方向サンプル数）

        static readonly Dictionary<int, AttackAnchor> _cache = new Dictionary<int, AttackAnchor>();

        public static AttackAnchor Get(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return default;
            int key = sprite.GetInstanceID();
            if (_cache.TryGetValue(key, out var cached)) return cached;

            AttackAnchor a = default;
            try { a = Analyze(sprite); }
            catch (System.Exception e) { Debug.LogWarning("[AttackAnchor] 解析失敗: " + e.Message); }
            _cache[key] = a;
            return a;
        }

        static AttackAnchor Analyze(Sprite sprite)
        {
            var tex = sprite.texture;
            // 実行時生成テクスチャのみ対象（非Readableな組み込み画像は解析せずフォールバック）
            if (!tex.isReadable) return default;

            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            int step = Mathf.Max(1, h / TargetSamples);

            // 列ごとの不透明ピクセル数と平均高さを取る（間引きサンプリング）
            int cols = (w + step - 1) / step;
            var colCount = new int[cols];
            var colYSum  = new float[cols];
            long total = 0, opaque = 0;
            for (int y = 0; y < h; y += step)
            {
                int row = y * w;
                for (int x = 0; x < w; x += step)
                {
                    total++;
                    if (px[row + x].a < AlphaThreshold255) continue;
                    opaque++;
                    int c = x / step;
                    colCount[c]++;
                    colYSum[c] += y;
                }
            }
            if (opaque < 20) return default;             // ほぼ透明＝解析不能
            if (opaque > total * 0.85f) return default;  // 背景が透過されていない画像（全面不透明）

            // 不透明列の範囲と最大列高さ
            int minC = -1, maxC = -1, maxColCount = 0;
            for (int c = 0; c < cols; c++)
            {
                if (colCount[c] == 0) continue;
                if (minC < 0) minC = c;
                maxC = c;
                maxColCount = Mathf.Max(maxColCount, colCount[c]);
            }
            if (maxC < 0 || maxColCount == 0) return default;

            // 「胴体」＝列の高さが最大値の45%以上ある帯。その前端より先の細い部分を得物とみなす。
            int bodyMinC = minC, bodyMaxC = maxC;
            for (int c = minC; c <= maxC; c++) if (colCount[c] >= maxColCount * 0.45f) { bodyMinC = c; break; }
            for (int c = maxC; c >= minC; c--) if (colCount[c] >= maxColCount * 0.45f) { bodyMaxC = c; break; }

            // 前方（右）と後方（左）の突出量。後方の方がずっと長い場合は素材が左向きに
            // 描かれている可能性が高く、前方アンカーとして信用しない（誤爆防止）。
            float rightLen = maxC - bodyMaxC;
            float leftLen  = bodyMinC - minC;
            if (leftLen > rightLen * 1.6f && leftLen * step > w * 0.08f) return default;

            // 先端の高さ: 最前列から少し内側までの帯（幅3%）の平均Y
            int band = Mathf.Max(1, Mathf.RoundToInt(cols * 0.03f));
            float tipYSum = 0f;
            int tipN = 0;
            for (int c = Mathf.Max(minC, maxC - band); c <= maxC; c++)
            {
                if (colCount[c] == 0) continue;
                tipYSum += colYSum[c] / colCount[c];
                tipN++;
            }
            if (tipN == 0) return default;

            // ピクセル→ローカル単位（ピボット=足元中央、PPU=高さ/2）
            float ppu   = h * 0.5f;
            float tipX  = (maxC * step - w * 0.5f) / ppu;
            float tipY  = (tipYSum / tipN) / ppu;
            float bodyX = Mathf.Max((bodyMaxC * step - w * 0.5f) / ppu, 0f);

            return new AttackAnchor
            {
                valid        = tipX > 0.05f, // 前方に何も突き出ていない画像はアンカー無効
                tip          = new Vector2(tipX, Mathf.Clamp(tipY, 0.1f, 2.4f)),
                bodyEdgeX    = bodyX,
                weaponLength = Mathf.Max(0f, tipX - bodyX),
            };
        }
    }
}
