using UnityEngine;
using System.Collections.Generic;

namespace PromptFighters.Battle.Skills
{
    // 一定時間だけ存在する近接攻撃判定。SkillExecutorが生成・破棄する。
    [RequireComponent(typeof(BoxCollider2D))]
    public class Hitbox : MonoBehaviour
    {
        public Fighter      Owner;
        public float        Damage;
        public float        Knockback;
        public Vector2      KnockbackDir = Vector2.right;
        public float        StunTime;
        public float        GuardDamage;
        public StatusType   Status = StatusType.None;
        public float        StatusDuration;
        public float        StatusChance = 1f;
        public Element      Element = Element.None;
        public Sprite       EffectSprite;
        public int          MaxHits  = 1;
        public float        Lifetime = 0.1f;

        readonly HashSet<Fighter> _hitTargets = new HashSet<Fighter>();
        int _hitsLanded;

        public static Hitbox Spawn(Fighter owner, Vector2 worldPos, Vector2 size, float lifetime)
        {
            var go = new GameObject("Hitbox");
            go.transform.position = worldPos;
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size      = Vector2.one; // スケールで大きさを制御するためcolliderは1x1

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = RuntimeSprite.Square();
            sr.color        = new Color(1f, 1f, 0f, 0.55f);
            sr.sortingOrder = 10;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);

            var hb = go.AddComponent<Hitbox>();
            hb.Owner    = owner;
            hb.Lifetime = lifetime;
            return hb;
        }

        void Start()
        {
            Color ec = SkillEnumParser.ElementColor(Element);
            var sr = GetComponent<SpriteRenderer>();
            if (EffectSprite != null)
            {
                sr.sprite = EffectSprite;
                sr.color = Color.white;
            }
            else
            {
                sr.color = new Color(ec.r, ec.g, ec.b, 0.65f);
            }
            Destroy(gameObject, Lifetime);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (_hitsLanded >= MaxHits) return;
            var target = other.GetComponent<Fighter>();
            if (target == null || target == Owner) return;
            if (_hitTargets.Contains(target)) return;

            _hitTargets.Add(target);
            ApplyHit(target);
            _hitsLanded++;
            if (_hitsLanded >= MaxHits)
            {
                // コライダーを無効化してビジュアルは lifetime まで表示し続ける
                var col = GetComponent<Collider2D>();
                if (col != null) col.enabled = false;
            }
        }

        void ApplyHit(Fighter target)
        {
            float dir = Mathf.Sign(target.transform.position.x - (Owner != null ? Owner.transform.position.x : transform.position.x));
            if (dir == 0f) dir = 1f;
            var kb = new Vector2(dir * Mathf.Abs(KnockbackDir.x), Mathf.Abs(KnockbackDir.y));

            target.TakeDamage(Damage, Knockback, kb, StunTime, GuardDamage);

            if (Status != StatusType.None && Random.value <= StatusChance)
                target.ApplyStatus(Status, StatusDuration);
        }
    }
}
