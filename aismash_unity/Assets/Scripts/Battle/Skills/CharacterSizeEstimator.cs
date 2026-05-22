using UnityEngine;

namespace PromptFighters.Battle.Skills
{
    // キャラクターの見た目テキストからサイズ倍率(0.7〜1.3)を推定する。
    public static class CharacterSizeEstimator
    {
        public static float Estimate(CharacterData data)
            => Estimate(data.inputFeatures);

        public static float Estimate(string features, string visualDesc = "", string visualPrompt = "")
        {
            // 体格はユーザーが明示した特徴のみから判定する（AI生成テキストは除外）
            string text = features.ToLower();
            float score = 0f;

            if (Has(text, "巨大", "大型", "giant", "huge", "massive", "enormous", "巨漢", "重量級", "大巨人"))
                score += 0.3f;
            if (Has(text, "体格の良い", "体格がいい", "筋肉", "マッチョ", "屈強", "大柄", "がっしり",
                         "muscular", "bulky", "large build", "broad"))
                score += 0.15f;
            if (Has(text, "小人", "チビ", "ちびっこ", "chibi", "dwarf", "tiny"))
                score -= 0.3f;
            if (Has(text, "小柄", "小さな", "小さい", "子供", "幼い", "petite", "small stature", "short stature"))
                score -= 0.2f;
            if (Has(text, "姿勢が低い", "低姿勢", "這い", "crouching", "low stance", "low posture"))
                score -= 0.1f;

            return Mathf.Clamp(1f + Mathf.Clamp(score, -0.3f, 0.3f), 0.7f, 1.3f);
        }

        static bool Has(string text, params string[] keywords)
        {
            foreach (var kw in keywords)
                if (text.Contains(kw)) return true;
            return false;
        }
    }
}
