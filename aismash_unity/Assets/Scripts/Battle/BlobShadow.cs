using UnityEngine;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // 地面・台に落ちる簡易ブロブ影。対象の真下にある最も近い地面/台/壁の上面を毎フレーム
    // 検出して落とし（台の上にいれば台の上面に、地上にいれば地面に）、対象が高く浮くほど
    // 小さく薄くする（立体感の補助）。対象が破棄されたら自動で消える。
    public class BlobShadow : MonoBehaviour
    {
        Transform _target;
        Collider2D _selfCollider; // 対象自身のコライダー。真下へのレイが自分自身に当たるのを防ぐため除外する。
        float _fallbackGroundY; // 真下に何も見つからなかった場合のフォールバック（地面Y+オフセット）
        float _yOffset;
        float _baseWidth;
        SpriteRenderer _sr;
        System.Func<float> _sizeProvider;

        const float MaxHeight = 6.5f;  // この高さで影がほぼ消える
        const float BaseAlpha = 0.34f;
        const float MaxRayDistance = 14f;

        static ContactFilter2D s_groundFilter;
        static bool s_filterInit;
        static readonly RaycastHit2D[] s_hitBuf = new RaycastHit2D[8];

        public static BlobShadow Spawn(Transform target, float groundY, float baseWidth,
            int sortingOrder, System.Func<float> sizeProvider = null, float yOffset = 0f)
        {
            var go = new GameObject("BlobShadow");
            var bs = go.AddComponent<BlobShadow>();
            bs._target          = target;
            bs._selfCollider    = target != null ? target.GetComponent<Collider2D>() : null;
            bs._yOffset         = yOffset;
            bs._fallbackGroundY = groundY + yOffset;
            bs._baseWidth       = baseWidth;
            bs._sizeProvider    = sizeProvider;

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
            float size    = _sizeProvider != null ? Mathf.Max(0.1f, _sizeProvider()) : 1f;
            float groundY = ResolveGroundY();
            float h       = Mathf.Max(0f, _target.position.y - groundY);
            float t       = Mathf.Clamp01(h / MaxHeight);
            float shrink  = Mathf.Lerp(1f, 0.5f, t) * size;

            transform.position   = new Vector3(_target.position.x, groundY, 0f);
            transform.localScale = new Vector3(_baseWidth * shrink, _baseWidth * 0.30f * shrink, 1f);
            if (_sr != null) _sr.color = new Color(0f, 0f, 0f, Mathf.Lerp(BaseAlpha, 0.05f, t));
        }

        // 対象の真下（足元ピボットから下方向）にレイを飛ばし、最も近い地面/台/壁の上面のYを返す。
        // 何にも当たらなければフォールバックのgroundYを使う。トリガーコライダー（当たり判定等）は無視する。
        float ResolveGroundY()
        {
            if (!s_filterInit)
            {
                s_groundFilter = new ContactFilter2D();
                s_groundFilter.useTriggers  = false;
                s_groundFilter.useLayerMask = false;
                s_filterInit = true;
            }

            int count = Physics2D.Raycast(_target.position, Vector2.down, s_groundFilter, s_hitBuf, MaxRayDistance);
            float best = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (s_hitBuf[i].collider == _selfCollider) continue; // 自分自身は地面として扱わない
                if (s_hitBuf[i].point.y > best) { best = s_hitBuf[i].point.y; found = true; }
            }
            return found ? best + _yOffset : _fallbackGroundY;
        }
    }
}
