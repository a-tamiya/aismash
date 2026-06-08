using UnityEngine;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // ダウンした味方の頭上に表示する復活ゲージ。仲間が寄り添っている間の進捗を示す。
    public class ReviveGauge : MonoBehaviour
    {
        const float Width  = 1.1f;
        const float Height = 0.16f;

        Transform _target;
        SpriteRenderer _fill;
        Transform _fillTf;

        public static ReviveGauge Create(Transform target)
        {
            var go = new GameObject("ReviveGauge");
            var gauge = go.AddComponent<ReviveGauge>();
            gauge._target = target;

            var bg = MakeBar(go.transform, new Color(0.05f, 0.06f, 0.09f, 0.85f), 30);
            bg.transform.localScale = new Vector3(Width, Height, 1f);

            var fillGo = MakeBar(go.transform, new Color(0.4f, 1f, 0.6f, 0.95f), 31);
            gauge._fill   = fillGo;
            gauge._fillTf = fillGo.transform;
            gauge._fillTf.localScale = new Vector3(0f, Height * 0.7f, 1f);

            gauge.SetProgress(0f);
            return gauge;
        }

        static SpriteRenderer MakeBar(Transform parent, Color color, int order)
        {
            var go = new GameObject("Bar");
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprite.Square();
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        public void SetProgress(float t01)
        {
            t01 = Mathf.Clamp01(t01);
            if (_fillTf == null) return;
            float w = Width * t01;
            _fillTf.localScale    = new Vector3(w, Height * 0.7f, 1f);
            // 左端基準で伸ばす
            _fillTf.localPosition = new Vector3(-(Width - w) * 0.5f, 0f, 0f);
        }

        void LateUpdate()
        {
            if (_target == null) { Destroy(gameObject); return; }
            transform.position = _target.position + Vector3.up * 1.5f;
            transform.rotation = Quaternion.identity;
        }
    }
}
