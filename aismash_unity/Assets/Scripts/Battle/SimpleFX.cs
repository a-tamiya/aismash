using UnityEngine;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // パーティクル不要の軽量ワンショットエフェクト。
    // ジャンプ・着地の土煙など、アクションの手応えを補強する小演出に使う。
    public static class SimpleFX
    {
        // 足元の土煙。数個の光球が外へ広がりながらフェードする。
        public static void Dust(Vector3 feetPos, int count = 4, float scale = 1f)
        {
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("DustPuff");
                go.transform.position = feetPos + new Vector3(Random.Range(-0.25f, 0.25f), 0.06f, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite       = RuntimeSprite.Glow();
                sr.color        = new Color(0.92f, 0.90f, 0.85f, 0.55f);
                sr.sortingOrder = 9;
                var p = go.AddComponent<DustPuff>();
                p.velocity  = new Vector2(Random.Range(-1.4f, 1.4f), Random.Range(0.4f, 1.2f)) * scale;
                p.baseScale = Random.Range(0.18f, 0.30f) * scale;
            }
        }
    }

    // 土煙の1粒。広がりつつ減速・フェードして消える。
    public class DustPuff : MonoBehaviour
    {
        public Vector2 velocity;
        public float   baseScale = 0.25f;
        const float Life = 0.35f;
        float _t;
        SpriteRenderer _sr;

        void Awake() => _sr = GetComponent<SpriteRenderer>();

        void Update()
        {
            _t += Time.deltaTime;
            float k = _t / Life;
            if (k >= 1f) { Destroy(gameObject); return; }
            transform.position += (Vector3)(velocity * Time.deltaTime);
            velocity *= Mathf.Max(0f, 1f - 2.5f * Time.deltaTime);
            transform.localScale = Vector3.one * (baseScale * (1f + k * 1.6f));
            if (_sr != null)
            {
                var c = _sr.color;
                c.a = 0.55f * (1f - k);
                _sr.color = c;
            }
        }
    }
}
