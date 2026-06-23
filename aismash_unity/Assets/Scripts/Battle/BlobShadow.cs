using UnityEngine;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // 地面に落ちる簡易ブロブ影。対象の真下（地面Y）に楕円を描き、対象が高く浮くほど
    // 小さく薄くする（立体感の補助）。対象が破棄されたら自動で消える。
    public class BlobShadow : MonoBehaviour
    {
        Transform _target;
        float _groundY;
        float _baseWidth;
        SpriteRenderer _sr;
        System.Func<float> _sizeProvider;

        const float MaxHeight = 6.5f;  // この高さで影がほぼ消える
        const float BaseAlpha = 0.34f;

        public static BlobShadow Spawn(Transform target, float groundY, float baseWidth,
            int sortingOrder, System.Func<float> sizeProvider = null, float yOffset = 0f)
        {
            var go = new GameObject("BlobShadow");
            var bs = go.AddComponent<BlobShadow>();
            bs._target        = target;
            bs._groundY       = groundY + yOffset; // 足元の下などへ微調整
            bs._baseWidth     = baseWidth;
            bs._sizeProvider  = sizeProvider;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite        = RuntimeSprite.Circle();
            sr.color         = new Color(0f, 0f, 0f, BaseAlpha);
            sr.sortingOrder  = sortingOrder;
            bs._sr = sr;

            bs.Apply();
            return bs;
        }

        void LateUpdate()
        {
            if (_target == null) { Destroy(gameObject); return; }
            // 対象が非表示なら影も隠す（非アクティブなボスの下に影が残らないように）。
            bool visible = _target.gameObject.activeInHierarchy;
            if (_sr != null && _sr.enabled != visible) _sr.enabled = visible;
            if (!visible) return;
            Apply();
        }

        void Apply()
        {
            float size   = _sizeProvider != null ? Mathf.Max(0.1f, _sizeProvider()) : 1f;
            float h      = Mathf.Max(0f, _target.position.y - _groundY);
            float t      = Mathf.Clamp01(h / MaxHeight);
            float shrink = Mathf.Lerp(1f, 0.5f, t) * size;

            transform.position   = new Vector3(_target.position.x, _groundY, 0f);
            transform.localScale = new Vector3(_baseWidth * shrink, _baseWidth * 0.30f * shrink, 1f);
            if (_sr != null) _sr.color = new Color(0f, 0f, 0f, Mathf.Lerp(BaseAlpha, 0.05f, t));
        }
    }
}
