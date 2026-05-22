using UnityEngine;
using System.Collections.Generic;
using PromptFighters.UI;

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

        // 追尾
        public Transform HomingTarget;
        public float     HomingStrength;

        // ブーメラン（寿命の半分で折り返す）
        public bool      IsBoomerang;

        // 重力スケール（0=無重力、1=通常）
        public float     GravityScale;

        // ノックバック方向（Hitbox と同じ仕組み）
        public Vector2   KnockbackDir = new Vector2(1f, 0.3f);
        public bool      FixedKnockbackDir;
        public bool      GroundBounce;

        SpriteRenderer _debugSr;
        float _spawnTime;
        bool  _boomerangFlipped;
        HashSet<Fighter> _boomerangHitSet;
        bool  _wasReflected;
        bool  _cancelled;

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

            var dbGo = new GameObject("ProjectileDebug");
            var dbSr = dbGo.AddComponent<SpriteRenderer>();
            dbSr.sprite       = RuntimeSprite.Square();
            dbSr.color        = new Color(1f, 0.35f, 0f, 0.6f);
            dbSr.sortingOrder = 12;
            dbSr.enabled      = false;
            p._debugSr = dbSr;

            return p;
        }

        void Start()
        {
            var sr = GetComponent<SpriteRenderer>();
            if (HideVisual)
            {
                sr.sprite  = RuntimeSprite.Square();
                sr.color   = new Color(1f, 0.35f, 0f, 0.55f);
                sr.enabled = false; // Update()で毎フレーム切り替え
                FitColliderAndVisualToWorldSize(sr);
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
            _spawnTime = Time.time;
            var rb2 = GetComponent<Rigidbody2D>();
            if (GravityScale > 0f) rb2.gravityScale = GravityScale;
            rb2.linearVelocity = Direction * Speed;
            Destroy(gameObject, Lifetime);
        }

        void Update()
        {
            // ブーメラン: 寿命の半分で折り返す
            if (IsBoomerang && !_boomerangFlipped && Time.time - _spawnTime >= Lifetime * 0.5f)
            {
                var rb = GetComponent<Rigidbody2D>();
                if (rb != null) { rb.linearVelocity = -rb.linearVelocity; Direction = -Direction; }
                _boomerangFlipped = true;
                _boomerangHitSet?.Clear(); // 復路で再ヒット可能に
                // 復路: オーナーへ強制追尾
                if (Owner != null)
                {
                    HomingTarget   = Owner.transform;
                    HomingStrength = Mathf.Max(HomingStrength, 0.65f);
                }
            }

            // ブーメラン復路: オーナーに近づいたら回収
            if (IsBoomerang && _boomerangFlipped && Owner != null)
            {
                Vector2 ownerCenter = (Vector2)Owner.transform.position + Vector2.up * 0.8f;
                if (Vector2.Distance(transform.position, ownerCenter) < 0.7f)
                    Destroy(gameObject);
            }

            // 追尾: 毎フレーム速度を目標方向へ曲げる
            if (HomingTarget != null && HomingStrength > 0f)
            {
                var rb = GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    Vector2 vel = rb.linearVelocity;
                    if (vel.sqrMagnitude > 0.01f)
                    {
                        Vector2 toTarget = (Vector2)HomingTarget.position + Vector2.up * 0.8f - (Vector2)transform.position;
                        float maxTurn = HomingStrength * 280f * Time.deltaTime;
                        float angle = Mathf.Clamp(Vector2.SignedAngle(vel, toTarget), -maxTurn, maxTurn);
                        rb.linearVelocity = (Vector2)(Quaternion.Euler(0f, 0f, angle) * vel);
                    }
                }
            }
        }

        void LateUpdate()
        {
            if (_debugSr == null) return;
            bool show = DebugSettings.ShowHitboxes;
            _debugSr.enabled = show;
            if (show)
            {
                var col = GetComponent<BoxCollider2D>();
                if (col != null)
                {
                    var b = col.bounds;
                    _debugSr.transform.position   = b.center;
                    _debugSr.transform.rotation   = Quaternion.identity;
                    _debugSr.transform.localScale  = new Vector3(b.size.x, b.size.y, 1f);
                }
            }
            if (!HideVisual)
            {
                var sr = GetComponent<SpriteRenderer>();
                if (sr != null) sr.enabled = !show;
            }
        }

        void OnDestroy()
        {
            if (_debugSr != null) Destroy(_debugSr.gameObject);
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
            if (_cancelled) return;

            // 飛び道具同士の相殺: 異なるオーナーの弾が衝突したら両方消滅
            var otherProj = other.GetComponent<Projectile>();
            if (otherProj != null && otherProj.Owner != Owner)
            {
                _cancelled = true;
                otherProj._cancelled = true;
                DamagePopup.SpawnText(transform.position, "相殺!", new Color(1f, 0.9f, 0.2f), 1.2f);
                Destroy(otherProj.gameObject);
                Destroy(gameObject);
                return;
            }

            var target = other.GetComponentInParent<Fighter>();
            if (target == null)
            {
                // 壁・地面に当たった場合: ブーメランは貫通、通常弾は消える
                if (!IsBoomerang && other.gameObject.layer != 0) Destroy(gameObject);
                return;
            }
            if (target == Owner)
            {
                if (IsBoomerang && _boomerangFlipped) Destroy(gameObject); // 回収
                return;
            }
            if (target.IsDodging) return;

            // リフレクター: 速度・威力を1.2倍にして逆ベクトルで反射、オーナーを切り替え（1回限り）
            if (!_wasReflected && target.IsReflecting)
            {
                var rb = GetComponent<Rigidbody2D>();
                if (rb != null) rb.linearVelocity = -rb.linearVelocity * 1.2f;
                Direction  = -Direction;
                Speed     *= 1.2f;
                Damage    *= 1.2f;
                Knockback *= 1.2f;
                Owner = target;
                _wasReflected = true;
                _boomerangHitSet?.Clear();
                DamagePopup.SpawnText(target.transform.position + Vector3.up * 0.5f, "REFLECT!", new Color(1f, 0.3f, 0.95f), 1.5f);
                return;
            }

            float dir = FixedKnockbackDir ? 1f : Mathf.Sign(Direction.x);
            if (dir == 0f) dir = 1f;
            var kb = new Vector2(dir * KnockbackDir.x * Knockback, KnockbackDir.y * Knockback);

            if (IsBoomerang)
            {
                // ブーメラン: 1パスにつき1ターゲット1回ヒット、消えずに継続
                if (_boomerangHitSet == null) _boomerangHitSet = new HashSet<Fighter>();
                if (_boomerangHitSet.Contains(target)) return;
                _boomerangHitSet.Add(target);
                target.TakeDamage(Damage, Knockback, kb, StunTime, GuardDamage, !DamageIncludesOwnerBoost);
                if (GroundBounce) target.StartGroundBounce(Knockback * 0.75f);
                if (Status != StatusType.None && Random.value <= StatusChance)
                    target.ApplyStatus(Status, StatusDuration);
                // 消えない
            }
            else
            {
                target.TakeDamage(Damage, Knockback, kb, StunTime, GuardDamage, !DamageIncludesOwnerBoost);
                if (GroundBounce) target.StartGroundBounce(Knockback * 0.75f);
                if (Status != StatusType.None && Random.value <= StatusChance)
                    target.ApplyStatus(Status, StatusDuration);
                Destroy(gameObject);
            }
        }
    }
}
