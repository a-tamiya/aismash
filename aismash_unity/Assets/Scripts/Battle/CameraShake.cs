using UnityEngine;

namespace PromptFighters.Battle
{
    // カメラ揺れ。ヒット・ガードブレイク・スマッシュ・KOの手応えを強調する。
    // KOズーム等の他のカメラ移動と共存できるよう、毎フレーム
    // 「前回足したオフセットを戻してから新しいオフセットを足す」方式で動く。
    public class CameraShake : MonoBehaviour
    {
        public static CameraShake Instance { get; private set; }

        float   _amplitude;
        float   _duration;
        float   _timer;
        Vector3 _prevOffset;

        // amplitude: 最大振れ幅(ワールド単位)。実行中はより強い要求のみ上書きする。
        public static void Shake(float amplitude, float duration)
        {
            if (Instance == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                Instance = cam.GetComponent<CameraShake>();
                if (Instance == null) Instance = cam.gameObject.AddComponent<CameraShake>();
            }
            if (Instance._timer > 0f && amplitude < Instance._amplitude) return;
            Instance._amplitude = amplitude;
            Instance._duration  = Mathf.Max(0.01f, duration);
            Instance._timer     = Instance._duration;
        }

        void Awake()
        {
            if (Instance == null) Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            // 前フレームのオフセットを戻す（他スクリプトのカメラ移動を壊さない）
            transform.position -= _prevOffset;
            _prevOffset = Vector3.zero;
            if (_timer <= 0f) return;

            // ヒットストップ（timeScale低下）中も揺れが進むよう unscaled で減衰
            _timer -= Time.unscaledDeltaTime;
            float k   = Mathf.Clamp01(_timer / _duration);
            float amp = _amplitude * k * k; // 二乗減衰で素早く収束
            float t   = Time.unscaledTime * 35f;
            _prevOffset = new Vector3(
                (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f,
                (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f,
                0f) * amp;
            transform.position += _prevOffset;
        }
    }
}
