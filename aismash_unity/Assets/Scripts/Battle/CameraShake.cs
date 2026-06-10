using UnityEngine;

namespace PromptFighters.Battle
{
    // カメラ揺れのオフセット計算。ヒット・ガードブレイク・スマッシュ・KOの手応えを強調する。
    // 実際のカメラへの適用は BattleCamera が毎フレーム行う（適用箇所を一本化し、
    // 動的ズーム・KOズームなど他のカメラ制御と競合しないようにする）。
    public static class CameraShake
    {
        static float _amplitude;
        static float _duration;
        static float _timer;

        // amplitude: 最大振れ幅(ワールド単位)。実行中はより強い要求のみ上書きする。
        public static void Shake(float amplitude, float duration)
        {
            if (_timer > 0f && amplitude < _amplitude) return;
            _amplitude = amplitude;
            _duration  = Mathf.Max(0.01f, duration);
            _timer     = _duration;
        }

        // 1フレーム分進めて現在の揺れオフセットを返す。
        // ヒットストップ（timeScale低下）中も揺れが進むよう unscaled の dt を渡すこと。
        public static Vector3 EvaluateOffset(float unscaledDeltaTime)
        {
            if (_timer <= 0f) return Vector3.zero;
            _timer -= unscaledDeltaTime;
            float k   = Mathf.Clamp01(_timer / _duration);
            float amp = _amplitude * k * k; // 二乗減衰で素早く収束
            float t   = Time.unscaledTime * 35f;
            return new Vector3(
                (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f,
                (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f,
                0f) * amp;
        }
    }
}
