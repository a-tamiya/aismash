using UnityEngine;

namespace PromptFighters.Battle.Skills
{
    // 飛び道具。Hitboxとは別物（移動する＆寿命管理）。
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public class Projectile : MonoBehaviour
    {
        public Fighter    Owner;
        public float      Damage;
        public float      Knockback;
        public float      StunTime;
        public float      GuardDamage;
        public StatusType Status = StatusType.None;
        public float      StatusDuration;
        public float      StatusChance = 1f;
        public Element    Element = Element.None;
        public Sprite     EffectSprite;
        public bool       FlipEffectX;
        public bool       HideVisual;
        public bool       DamageIncludesOwnerBoost;
        public float      Speed     = 8f;
        public float      Lifetime  = 2f;
        public Vector2    Direction = Vector2.right;
        public Vector2    DesiredWorldSize = new Vector2(1.2f, 0.74f);

        public static Projectile Spawn(Fighter owner, Vector2 worldPos, Vector2 dir,
                                       float speed, float lifetime)
        {
            var go = new GameObject("Projectile");
            go.transform.position = worldPos;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.constraints  = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size      = Vector2.one;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = RuntimeSprite.Square();
            sr.sortingOrder = 10;
            go.transform.localScale = new Vector3(0.84f, 0.62f, 1f);

            var p = go.AddComponent<Projectile>();
            p.Owner     = owner;
            p.Direction = dir.normalized;
            p.Speed     = speed;
            p.Lifetime  = lifetime;
            return p;
        }

        void Start()
        {
            var sr = GetComponent<SpriteRenderer>();
            if (HideVisual)
            {
                if (DebugSettings.ShowHitboxes && sr != null)
                {
                    sr.enabled = true;
                    sr.sprite  = RuntimeSprite.Square();
                    sr.color   = new Color(1f, 0.35f, 0f, 0.55f);
                    FitColliderAndVisualToWorldSize(sr);
                }
                else
                {
                    if (sr != null) sr.enabled = false;
                    FitColliderToDesiredWorldSize();
                }
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
                sr.color = SkillEnumParser.ElementColor(Element);
                FitColliderAndVisualToWorldSize(sr);
            }
            GetComponent<Rigidbody2D>().linearVelocity = Direction * Speed;
            Destroy(gameObject, Lifetime);
        }

        void FitColliderToDesiredWorldSize()
        {
            var col = GetComponent<BoxCollider2D>();
            if (col == null) return;
            col.size = Vector2.one;
            col.offset = Vector2.zero;
            transform.localScale = new Vector3(
                Mathf.Max(0.05f, DesiredWorldSize.x),
                Mathf.Max(0.05f, DesiredWorldSize.y),
                1f);
        }

        void FitColliderAndVisualToWorldSize(SpriteRenderer sr)
        {
            var col = GetComponent<BoxCollider2D>();
            if (col == null || sr?.sprite == null) return;

            Vector2 spriteSize = sr.sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f) return;

            col.size = spriteSize;
            col.offset = Vector2.zero;
            transform.localScale = new Vector3(
                DesiredWorldSize.x / spriteSize.x,
                DesiredWorldSize.y / spriteSize.y,
                1f);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var target = other.GetComponentInParent<Fighter>();
            if (target == null)
            {
                // 壁・地面に当たった場合も消える
                if (other.gameObject.layer != 0) Destroy(gameObject);
                return;
            }
            if (target == Owner) return;
            if (target.IsDodging) return;

            float dir = Mathf.Sign(Direction.x);
            if (dir == 0f) dir = 1f;
            var kb = new Vector2(dir * Knockback, Knockback * 0.3f);

            target.TakeDamage(Damage, Knockback, kb, StunTime, GuardDamage, !DamageIncludesOwnerBoost);
            if (Status != StatusType.None && Random.value <= StatusChance)
                target.ApplyStatus(Status, StatusDuration);

            Destroy(gameObject);
        }
    }
}
