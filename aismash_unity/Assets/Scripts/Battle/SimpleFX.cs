using UnityEngine;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // パーティクル不要の軽量ワンショットエフェクト。
    // 砂煙・ジャンプ等の手応え演出に使う。画像（Resources/Effects/*）があればそれを、
    // 無ければ従来のグロー球にフォールバックする。
    public static class SimpleFX
    {
        static Sprite _dust, _jumpGround, _jumpAir;
        static bool   _dustTried, _jgTried, _jaTried;

        static Sprite DustSprite()   { if (!_dustTried) { _dust = Resources.Load<Sprite>("Effects/dust");       _dustTried = true; } return _dust; }
        static Sprite JumpGSprite()  { if (!_jgTried)   { _jumpGround = Resources.Load<Sprite>("Effects/jump_ground"); _jgTried = true; } return _jumpGround; }
        static Sprite JumpASprite()  { if (!_jaTried)   { _jumpAir = Resources.Load<Sprite>("Effects/jump_air");   _jaTried = true; } return _jumpAir; }

        // 汎用エフェクト（Resources/Effects/*）。無ければ何も出さない。
        static readonly System.Collections.Generic.Dictionary<string, Sprite> _cache =
            new System.Collections.Generic.Dictionary<string, Sprite>();
        static Sprite Fx(string name)
        {
            if (_cache.TryGetValue(name, out var s)) return s;
            s = Resources.Load<Sprite>("Effects/" + name);
            _cache[name] = s;
            return s;
        }

        // ヒット火花。命中位置に一瞬パッと出る。
        public static void HitSpark(Vector3 pos, float scale = 1f)
        {
            var s = Fx("hit_spark"); if (s == null) return;
            Spawn(s, pos, 1.7f * scale, 0.6f, 1.6f, 0.28f, Color.white, 12, 0f);
        }

        // ガードブレイク。盾が砕ける。
        public static void GuardBreak(Vector3 pos)
        {
            var s = Fx("guard_break"); if (s == null) return;
            Spawn(s, pos, 2.8f, 0.5f, 1.7f, 0.5f, Color.white, 12, 0.3f);
        }

        // 着地・重撃の衝撃波。
        public static void Shockwave(Vector3 feetPos, float scale = 1f)
        {
            var s = Fx("shockwave"); if (s == null) { Dust(feetPos, 3, scale); return; }
            Spawn(s, feetPos + Vector3.up * 0.12f, 3.0f * scale, 0.4f, 1.7f, 0.4f, Color.white, 8, 0f);
        }

        // 強化（上昇）・弱体（下降）オーラの一瞬の演出。
        public static void Buff(Vector3 pos)
        {
            var s = Fx("buff_up"); if (s == null) return;
            Spawn(s, pos, 2.2f, 0.7f, 1.15f, 0.7f, Color.white, 12, 1.0f);
        }
        public static void Debuff(Vector3 pos)
        {
            var s = Fx("debuff_down"); if (s == null) return;
            Spawn(s, pos, 2.2f, 0.7f, 1.15f, 0.7f, Color.white, 12, -0.6f);
        }

        // スタン（星）を頭上に。
        public static void StunStars(Vector3 headPos)
        {
            var s = Fx("aura_stun"); if (s == null) return;
            Spawn(s, headPos, 1.8f, 0.6f, 1.05f, 0.7f, Color.white, 13, 0.1f);
        }

        // カウンター成功・反射の閃光。
        public static void CounterFlash(Vector3 pos)
        {
            var s = Fx("counter"); if (s == null) return;
            Spawn(s, pos, 2.2f, 0.5f, 1.5f, 0.32f, Color.white, 13, 0f);
        }
        public static void ReflectFlash(Vector3 pos)
        {
            var s = Fx("reflect"); if (s == null) return;
            Spawn(s, pos, 2.6f, 0.6f, 1.3f, 0.45f, Color.white, 12, 0f);
        }

        // スマッシュの溜め/発動の閃光。
        public static void SmashFlash(Vector3 pos)
        {
            var s = Fx("smash_charge"); if (s == null) return;
            Spawn(s, pos, 2.6f, 0.6f, 1.4f, 0.4f, Color.white, 12, 0f);
        }

        // 着地などの砂煙。
        public static void Dust(Vector3 feetPos, int count = 1, float scale = 1f)
        {
            var s = DustSprite();
            if (s == null) { GlowFallback(feetPos, Mathf.Max(count, 3), scale); return; }
            Spawn(s, feetPos + Vector3.up * 0.25f, 1.6f * scale, 0.55f, 1.3f, 0.45f,
                  new Color(1f, 1f, 1f, 0.95f), 9, 0.32f);
        }

        // 地上ジャンプ：横に広がる踏み込みの煙。
        public static void JumpGround(Vector3 feetPos)
        {
            var s = JumpGSprite();
            if (s == null) { Dust(feetPos, 3, 0.9f); return; }
            Spawn(s, feetPos + Vector3.up * 0.25f, 2.8f, 0.7f, 1.12f, 0.34f,
                  new Color(1f, 1f, 1f, 0.95f), 9, 0.12f);
        }

        // 空中ジャンプ：エネルギーリング。
        public static void JumpAir(Vector3 feetPos)
        {
            var s = JumpASprite();
            if (s == null) { Dust(feetPos, 3, 0.8f); return; }
            Spawn(s, feetPos + Vector3.up * 0.45f, 2.1f, 0.5f, 1.5f, 0.4f,
                  Color.white, 9, 0f);
        }

        static void Spawn(Sprite sprite, Vector3 pos, float worldWidth, float startMul, float endMul,
                          float life, Color color, int sorting, float rise)
        {
            var go = new GameObject("FX");
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite; sr.color = color; sr.sortingOrder = sorting;
            float baseScale = worldWidth / Mathf.Max(0.01f, sprite.bounds.size.x);
            go.AddComponent<OneShotFx>().Init(baseScale * startMul, baseScale * endMul, life, color, rise);
        }

        // 画像が無い場合の従来グロー（保険）。
        static void GlowFallback(Vector3 feetPos, int count, float scale)
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

    // 1枚のスプライトを拡大しながらフェードして消すワンショット演出。
    public class OneShotFx : MonoBehaviour
    {
        float _life = 0.4f, _t, _s0 = 1f, _s1 = 1.3f, _rise;
        Color _c0 = Color.white;
        SpriteRenderer _sr;

        public void Init(float startScale, float endScale, float life, Color color, float rise)
        {
            _s0 = startScale; _s1 = endScale; _life = Mathf.Max(0.05f, life); _c0 = color; _rise = rise;
            _sr = GetComponent<SpriteRenderer>();
            transform.localScale = Vector3.one * _s0;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = _t / _life;
            if (k >= 1f) { Destroy(gameObject); return; }
            transform.localScale = Vector3.one * Mathf.Lerp(_s0, _s1, k);
            if (_rise != 0f) transform.position += Vector3.up * (_rise * Time.deltaTime);
            if (_sr != null) { var c = _c0; c.a = _c0.a * (1f - k); _sr.color = c; }
        }
    }

    // 土煙の1粒（グローフォールバック用）。広がりつつ減速・フェードして消える。
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
