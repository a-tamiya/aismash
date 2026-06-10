using UnityEngine;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;

namespace PromptFighters.Battle
{
    // 音声アイテム（スマッシュボール式）。
    // 空中をふわふわ漂い、攻撃で耐久(MaxHP)を削られる。耐久を0にした
    // 「最後の攻撃者」が取得者となり、onBroken で通知する。
    // Hitbox / Projectile から TakeHit(dmg, attacker) を呼ばれる（陣営問わず誰でも殴れる中立物）。
    [RequireComponent(typeof(BoxCollider2D))]
    public class VoiceItem : MonoBehaviour
    {
        public const float MaxHP = 30f;

        float   _hp = MaxHP;
        Fighter _lastAttacker;
        bool    _broken;
        System.Action<Fighter> _onBroken;

        SpriteRenderer _outer, _inner;
        Vector3 _basePos;
        float   _seed;
        float   _driftDir = 1f;
        float   _halfRangeX;

        static readonly Color GoldGlow = new Color(1f, 0.82f, 0.18f, 0.9f);

        public static VoiceItem Spawn(Vector2 pos, float halfRangeX, System.Action<Fighter> onBroken)
        {
            var go = new GameObject("VoiceItem");
            go.transform.position = pos;

            var item = go.AddComponent<VoiceItem>();
            item._onBroken   = onBroken;
            item._basePos    = pos;
            item._seed       = Random.value * 10f;
            item._driftDir   = Random.value < 0.5f ? -1f : 1f;
            item._halfRangeX = Mathf.Max(1f, halfRangeX);

            // 外側の大きなグロー（子オブジェクト。ルートは脈動・回転のみ担当）
            var outerGo = new GameObject("Glow");
            outerGo.transform.SetParent(go.transform, false);
            item._outer = outerGo.AddComponent<SpriteRenderer>();
            item._outer.sprite = RuntimeSprite.Glow();
            item._outer.color  = GoldGlow;
            item._outer.sortingOrder = 9;
            FitSprite(item._outer, 2.6f);

            // 内側の明るいコア
            var innerGo = new GameObject("Core");
            innerGo.transform.SetParent(go.transform, false);
            item._inner = innerGo.AddComponent<SpriteRenderer>();
            item._inner.sprite = RuntimeSprite.Glow();
            item._inner.color  = new Color(1f, 0.98f, 0.85f, 1f);
            item._inner.sortingOrder = 10;
            FitSprite(item._inner, 1.2f);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.7f, 1.7f);

            return item;
        }

        static void FitSprite(SpriteRenderer sr, float worldDiameter)
        {
            float s = sr.sprite != null ? Mathf.Max(0.01f, sr.sprite.bounds.size.x) : 1f;
            sr.transform.localScale = Vector3.one * (worldDiameter / s);
        }

        void Update()
        {
            float t = Time.time + _seed;

            // ふわふわ上下＋ゆっくり横ドリフト（ステージ内で反復）
            float bob = Mathf.Sin(t * 1.6f) * 0.5f;
            _basePos.x += _driftDir * 0.7f * Time.deltaTime;
            if (_basePos.x >  _halfRangeX) { _basePos.x =  _halfRangeX; _driftDir = -1f; }
            if (_basePos.x < -_halfRangeX) { _basePos.x = -_halfRangeX; _driftDir =  1f; }
            transform.position = new Vector3(_basePos.x, _basePos.y + bob, 0f);

            // 脈動＋回転で目立たせる
            float pulse = 1f + 0.12f * Mathf.Sin(t * 6f);
            transform.localScale = Vector3.one * pulse;
            transform.Rotate(0f, 0f, 60f * Time.deltaTime);

            // 残り耐久が減るほど内側コアを明るく＝割れる直前感
            float dmgRatio = 1f - Mathf.Clamp01(_hp / MaxHP);
            if (_inner != null)
                _inner.color = Color.Lerp(new Color(1f, 0.98f, 0.85f, 1f), Color.white, dmgRatio);
        }

        public void TakeHit(float dmg, Fighter attacker)
        {
            if (_broken || dmg <= 0f) return;
            if (attacker != null) _lastAttacker = attacker;
            _hp -= dmg;

            DamagePopup.SpawnText(transform.position + Vector3.up * 0.7f,
                Mathf.RoundToInt(dmg).ToString(), new Color(1f, 0.8f, 0.2f), 1.0f);
            CameraShake.Shake(0.08f, 0.12f);

            if (_hp <= 0f)
            {
                _broken = true;
                DamagePopup.SpawnText(transform.position + Vector3.up * 1.0f,
                    "GET!!", new Color(1f, 0.85f, 0.15f), 2.0f);
                CameraShake.Shake(0.3f, 0.25f);
                _onBroken?.Invoke(_lastAttacker);
                Destroy(gameObject);
            }
        }
    }
}
