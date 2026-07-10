using UnityEngine;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // ダウン中の味方の頭上に出す目立つ下矢印マーカー。上下にバウンドしつつ明滅させ、
    // 画面のどこにいても「救助が必要な味方がここにいる」と一目で分かるようにする。
    // 復活/対象消滅で自動的に自身を破棄する。
    public class DownedMarker : MonoBehaviour
    {
        Fighter _target;
        SpriteRenderer _sr;
        float _seed;

        public static DownedMarker Create(Fighter target)
        {
            var go = new GameObject("DownedMarker");
            var marker = go.AddComponent<DownedMarker>();
            marker._target = target;
            marker._seed = Random.value * 10f;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprite.DownArrow();
            sr.color = new Color(1f, 0.85f, 0.2f, 0.95f);
            sr.sortingOrder = 32;
            marker._sr = sr;
            go.transform.localScale = Vector3.one * 0.55f;
            return marker;
        }

        void LateUpdate()
        {
            if (_target == null || !_target.IsDowned) { Destroy(gameObject); return; }

            float t = Time.time + _seed;
            float bob = Mathf.Abs(Mathf.Sin(t * 4f)) * 0.18f;
            transform.position = _target.ReviveGaugePosition + Vector3.up * (0.4f + bob);
            transform.rotation = Quaternion.identity;

            if (_sr != null)
            {
                float pulse = (Mathf.Sin(t * 7f) + 1f) * 0.5f;
                _sr.color = new Color(1f, 0.85f, 0.2f, Mathf.Lerp(0.65f, 1f, pulse));
                float scalePulse = Mathf.Lerp(0.5f, 0.62f, pulse);
                transform.localScale = Vector3.one * scalePulse;
            }
        }
    }
}
