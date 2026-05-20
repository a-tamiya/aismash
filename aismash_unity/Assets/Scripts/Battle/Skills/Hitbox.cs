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
        public bool         FlipEffectX;
        public int          MaxHits  = 1;
        public float        Lifetime = 0.1f;
        public bool         FollowOwner;
        public bool         HideVisual;
        public bool         DamageIncludesOwnerBoost;
        public Vector2      OwnerLocalOffset;
        public Vector2      DesiredWorldSize;

        readonly HashSet<Fighter> _hitTargets = new HashSet<Fighter>();
        readonly Dictionary<Fighter, float> _nextHitTimes = new Dictionary<Fighter, float>();
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
            hb.DesiredWorldSize = size;
            return hb;
        }

        void Start()
        {
            Color ec = SkillEnumParser.ElementColor(Element);
            var sr = GetComponent<SpriteRenderer>();
            if (HideVisual)
            {
                if (DebugSettings.ShowHitboxes && sr != null)
                {
                    sr.enabled = true;
                    sr.sprite  = RuntimeSprite.Square();
                    sr.color   = new Color(1f, 0.35f, 0f, 0.55f); // 橙: 食らわせ判定
                }
                else if (sr != null) sr.enabled = false;
            }
            else if (EffectSprite != null)
            {
                sr.sprite = EffectSprite;
                sr.color = Color.white;
                sr.flipX = FlipEffectX;
                FitColliderAndVisualToWorldSize(sr);
            }
            else
            {
                sr.color = new Color(ec.r, ec.g, ec.b, 0.65f);
            }
            Destroy(gameObject, Lifetime);
        }

        void LateUpdate()
        {
            if (!FollowOwner || Owner == null) return;
            float dirSign = Owner.FacingRight ? 1f : -1f;
            transform.position = (Vector2)Owner.transform.position +
                new Vector2(dirSign * OwnerLocalOffset.x, OwnerLocalOffset.y);
        }

        void FitColliderAndVisualToWorldSize(SpriteRenderer sr)
        {
            var col = GetComponent<BoxCollider2D>();
            if (col == null || sr?.sprite == null) return;

            Vector2 spriteSize = sr.sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f) return;

            Vector2 targetSize = DesiredWorldSize;
            if (targetSize.x <= 0f || targetSize.y <= 0f)
                targetSize = spriteSize;

            col.size = spriteSize;
            col.offset = Vector2.zero;
            transform.localScale = new Vector3(
                targetSize.x / spriteSize.x,
                targetSize.y / spriteSize.y,
                1f);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            TryHit(other);
        }

        void OnTriggerStay2D(Collider2D other)
        {
            TryHit(other);
        }

        void TryHit(Collider2D other)
        {
            if (_hitsLanded >= MaxHits) return;
            var target = other.GetComponentInParent<Fighter>();
            if (target == null || target == Owner) return;
            if (target.IsDodging) return;
            if (MaxHits <= 1 && _hitTargets.Contains(target)) return;
            if (MaxHits > 1 &&
                _nextHitTimes.TryGetValue(target, out float nextTime) &&
                Time.time < nextTime) return;

            _hitTargets.Add(target);
            ApplyHit(target);
            _hitsLanded++;
            if (MaxHits > 1)
                _nextHitTimes[target] = Time.time + Mathf.Max(0.04f, Lifetime / Mathf.Max(1, MaxHits));
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

            target.TakeDamage(Damage, Knockback, kb, StunTime, GuardDamage, !DamageIncludesOwnerBoost);

            if (Status != StatusType.None && Random.value <= StatusChance)
                target.ApplyStatus(Status, StatusDuration);
        }
    }
}
