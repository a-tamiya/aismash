using UnityEngine;
using System.Collections.Generic;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;

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
        public bool    PlayerControlled;
        public bool    Homing;
        public string  Direction;
        public string  KnockbackDirection;
        public StatusType Status = StatusType.None;
        public float   StatusDuration;
        public float   StatusChance = 1f;

        public const float MaxHP = 10f;
        float _hp = MaxHP;

        Rigidbody2D _rb;
        float _startX;
        float _dir = 1f;
        readonly HashSet<Fighter> _recentHits = new HashSet<Fighter>();

        public static SummonEntity Spawn(Fighter owner, Vector2 pos, float speed, float lifetime,
                                         float damage, float knockback, Element element,
                                         Sprite sprite = null, Vector2? desiredWorldSize = null,
                                         SkillAction sourceAction = null)
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
            if (sourceAction != null)
            {
                s.PlayerControlled   = sourceAction.player_controlled;
                s.Homing             = sourceAction.homing;
                s.Direction          = sourceAction.direction;
                s.KnockbackDirection = sourceAction.knockback_direction;
                s.Status             = SkillEnumParser.ParseStatus(sourceAction.status);
                s.StatusDuration     = sourceAction.status_duration > 0f
                    ? sourceAction.status_duration
                    : Mathf.Min(sourceAction.duration, 3f);
                s.StatusChance       = Mathf.Clamp01(sourceAction.chance);
            }

            Object.Destroy(go, lifetime);
            return s;
        }

        void Start()
        {
            _rb     = GetComponent<Rigidbody2D>();
            _startX = transform.position.x;
            _dir    = InitialDirection();
            _rb.linearVelocity = new Vector2(_dir * Speed, 0f);
        }

        void Update()
        {
            if (PlayerControlled && Owner != null)
            {
                float input = Owner.LastMoveInputX;
                if (Owner.InputReversed) input = -input;
                if (Mathf.Abs(input) > 0.1f)
                {
                    _dir = Mathf.Sign(input);
                    _rb.linearVelocity = new Vector2(_dir * Speed, 0f);
                    GetComponent<SpriteRenderer>().flipX = _dir < 0;
                    return;
                }
            }

            if (Homing && Owner != null && Owner.Opponent != null)
            {
                float dx = Owner.Opponent.transform.position.x - transform.position.x;
                if (Mathf.Abs(dx) > 0.05f)
                {
                    _dir = Mathf.Sign(dx);
                    _rb.linearVelocity = new Vector2(_dir * Speed, 0f);
                    GetComponent<SpriteRenderer>().flipX = _dir < 0;
                    return;
                }
            }

            if (Direction == "stationary")
            {
                _rb.linearVelocity = Vector2.zero;
                return;
            }

            float distX = transform.position.x - _startX;
            if ((_dir > 0 && distX > PatrolRange) || (_dir < 0 && distX < -PatrolRange))
            {
                _dir = -_dir;
                _rb.linearVelocity = new Vector2(_dir * Speed, 0f);
                GetComponent<SpriteRenderer>().flipX = _dir < 0;
            }
        }

        public void TakeHit(float dmg)
        {
            if (dmg <= 0f) return;
            _hp -= dmg;
            DamagePopup.SpawnText(transform.position + Vector3.up * 0.6f,
                Mathf.RoundToInt(dmg).ToString(), new Color(1f, 0.45f, 0.1f), 1.0f);
            if (_hp <= 0f)
            {
                DamagePopup.SpawnText(transform.position + Vector3.up * 1.0f,
                    "破壊!", new Color(1f, 0.65f, 0.1f), 1.6f);
                Destroy(gameObject);
            }
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var target = other.GetComponentInParent<Fighter>();
            if (target == null || target == Owner) return;
            // フレンドリーファイアOFF：同陣営には当てない
            if (Owner != null && target.Team == Owner.Team) return;
            if (target.IsDodging) return;
            if (_recentHits.Contains(target)) return;

            _recentHits.Add(target);
            Vector2 kb = KnockbackVector(target);
            target.TakeDamage(Damage, Knockback, kb, 0.12f, Damage * 0.3f, false);
            if (Status != StatusType.None && Random.value <= StatusChance)
                target.ApplyStatus(Status, StatusDuration);
            Invoke(nameof(ClearHits), 0.55f);
        }

        void ClearHits() => _recentHits.Clear();

        float InitialDirection()
        {
            if (Direction == "backward") return Owner != null && Owner.FacingRight ? -1f : 1f;
            if (Direction == "left") return -1f;
            if (Direction == "right") return 1f;
            if (Direction == "toward_enemy" && Owner?.Opponent != null)
                return Mathf.Sign(Owner.Opponent.transform.position.x - transform.position.x);
            if (Direction == "away_enemy" && Owner?.Opponent != null)
                return -Mathf.Sign(Owner.Opponent.transform.position.x - transform.position.x);
            return Owner != null && !Owner.FacingRight ? -1f : 1f;
        }

        Vector2 KnockbackVector(Fighter target)
        {
            float facing = Mathf.Sign(_dir);
            if (Mathf.Approximately(facing, 0f)) facing = 1f;
            return KnockbackDirection switch
            {
                "up"            => new Vector2(0f, 1.5f),
                "spike"         => new Vector2(facing * 0.15f, -1.2f),
                "toward"        => Owner != null ? new Vector2(Mathf.Sign(Owner.transform.position.x - target.transform.position.x), 0.35f) : new Vector2(-facing, 0.35f),
                "diagonal_up"   => new Vector2(facing * 0.45f, 1.15f),
                "ground_bounce" => new Vector2(facing * 0.25f, -1.4f),
                _               => new Vector2(facing, 0.3f),
            };
        }
    }
}
