using UnityEngine;
using System.Collections.Generic;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // 召喚技で生成されるエンティティ。左右にパトロールし、敵に触れるとダメージを与える。
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public class SummonEntity : MonoBehaviour
    {
        public Fighter Owner;
        public float   Damage;
        public float   Knockback;
        public float   Speed = 2.5f;
        public float   PatrolRange = 3f;
        public Element Element = Element.None;

        Rigidbody2D _rb;
        float _startX;
        float _dir = 1f;
        readonly HashSet<Fighter> _recentHits = new HashSet<Fighter>();

        public static SummonEntity Spawn(Fighter owner, Vector2 pos, float speed, float lifetime,
                                         float damage, float knockback, Element element,
                                         Sprite sprite = null, Vector2? desiredWorldSize = null)
        {
            var go = new GameObject("SummonEntity");
            go.transform.position = pos;
            go.layer = owner.gameObject.layer;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.constraints  = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionY;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite != null ? sprite : RuntimeSprite.Square();
            if (sprite == null)
            {
                Color ec = SkillEnumParser.ElementColor(element);
                sr.color = new Color(ec.r * 0.7f + 0.3f, ec.g * 0.5f, ec.b * 0.7f + 0.3f, 0.82f);
            }
            else
            {
                sr.color = Color.white;
            }
            sr.sortingOrder = 8;

            Vector2 worldSize = desiredWorldSize ?? new Vector2(0.9f, 1.2f);
            Vector2 spriteSize = sr.sprite != null
                ? new Vector2(Mathf.Max(0.01f, sr.sprite.bounds.size.x), Mathf.Max(0.01f, sr.sprite.bounds.size.y))
                : Vector2.one;
            go.transform.localScale = new Vector3(worldSize.x / spriteSize.x, worldSize.y / spriteSize.y, 1f);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = spriteSize;

            var s = go.AddComponent<SummonEntity>();
            s.Owner      = owner;
            s.Speed      = Mathf.Max(0.5f, speed);
            s.Damage     = damage;
            s.Knockback  = knockback;
            s.Element    = element;

            Object.Destroy(go, lifetime);
            return s;
        }

        void Start()
        {
            _rb     = GetComponent<Rigidbody2D>();
            _startX = transform.position.x;
            _dir    = Owner != null && !Owner.FacingRight ? -1f : 1f;
            _rb.linearVelocity = new Vector2(_dir * Speed, 0f);
        }

        void Update()
        {
            float distX = transform.position.x - _startX;
            if ((_dir > 0 && distX > PatrolRange) || (_dir < 0 && distX < -PatrolRange))
            {
                _dir = -_dir;
                _rb.linearVelocity = new Vector2(_dir * Speed, 0f);
                GetComponent<SpriteRenderer>().flipX = _dir < 0;
            }
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var target = other.GetComponentInParent<Fighter>();
            if (target == null || target == Owner) return;
            if (target.IsDodging) return;
            if (_recentHits.Contains(target)) return;

            _recentHits.Add(target);
            float dir = Mathf.Sign(_dir);
            Vector2 kb = new Vector2(dir, 0.3f);
            target.TakeDamage(Damage, Knockback, kb, 0.12f, Damage * 0.3f, false);
            Invoke(nameof(ClearHits), 0.55f);
        }

        void ClearHits() => _recentHits.Clear();
    }
}
