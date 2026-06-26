using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using PromptFighters.UI;

namespace PromptFighters.Battle.Skills
{
    // 飛び道具。Hitboxとは別物（移動する＆寿命管理）。
    // GC負荷軽減のためオブジェクトプールで再利用する。
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
        SpriteRenderer _sr;
        Rigidbody2D    _rb;
        BoxCollider2D  _col;
        float _spawnTime;
        bool  _boomerangFlipped;
        HashSet<Fighter> _boomerangHitSet;
        bool  _wasReflected;
        bool  _cancelled;
        bool  _released;
        bool  _activated;

        static readonly Stack<Projectile> s_pool = new Stack<Projectile>();

        public static Projectile Spawn(Fighter owner, Vector2 worldPos, Vector2 dir,
                                       float speed, float lifetime)
        {
            var p = Acquire();
            p.transform.position = worldPos;
            p.transform.localScale = new Vector3(0.84f, 0.62f, 1f);

            p._sr.sprite  = RuntimeSprite.Square();
            p._sr.enabled = false; // アクティベート完了まで描画しない（1フレーム点滅防止）

            p.Owner     = owner;
            p.Direction = dir.normalized;
            p.Speed     = speed;
            p.Lifetime  = lifetime;
            p.BeginDeferredActivate();
            return p;
        }

        static Projectile Acquire()
        {
            Projectile p = null;
            while (s_pool.Count > 0)
            {
                p = s_pool.Pop();
                if (p != null) break; // 破棄済み（シーン遷移等）はスキップ
            }
            if (p == null) p = Create();
            p.ResetState();
            p.gameObject.SetActive(true);
            return p;
        }

        static Projectile Create()
        {
            var go = new GameObject("Projectile");

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

            var p = go.AddComponent<Projectile>();
            p._rb  = rb;
            p._col = col;
            p._sr  = sr;

            var dbGo = new GameObject("ProjectileDebug");
            var dbSr = dbGo.AddComponent<SpriteRenderer>();
            dbSr.sprite       = RuntimeSprite.Square();
            dbSr.color        = new Color(1f, 0.35f, 0f, 0.6f);
            dbSr.sortingOrder = 12;
            dbSr.enabled      = false;
            p._debugSr = dbSr;

            return p;
        }

        // 再利用前に全状態を初期化する
        void ResetState()
        {
            _released = false;
            _activated = false;
            _boomerangFlipped = false;
            _wasReflected = false;
            _cancelled = false;
            _boomerangHitSet?.Clear();

            Owner = null;
            Damage = 0f;
            Knockback = 0f;
            StunTime = 0f;
            GuardDamage = 0f;
            Status = StatusType.None;
            StatusDuration = 0f;
            StatusChance = 1f;
            Element = Element.None;
            EffectSprite = null;
            FlipEffectX = false;
            HideVisual = false;
            DamageIncludesOwnerBoost = false;
            Speed = 8f;
            Lifetime = 2f;
            Direction = Vector2.right;
            DesiredWorldSize = new Vector2(1.2f, 0.74f);
            HomingTarget = null;
            HomingStrength = 0f;
            IsBoomerang = false;
            GravityScale = 0f;
            KnockbackDir = new Vector2(1f, 0.3f);
            FixedKnockbackDir = false;
            GroundBounce = false;

            transform.rotation = Quaternion.identity;
            if (_rb != null)
            {
                _rb.linearVelocity = Vector2.zero;
                _rb.gravityScale   = 0f;
            }
            if (_col != null)
            {
                _col.enabled = true;
                _col.size    = Vector2.one;
                _col.offset  = Vector2.zero;
            }
        }

        void BeginDeferredActivate()
        {
            StopAllCoroutines();
            StartCoroutine(DeferredActivate());
        }

        // 旧Start()相当。呼び出し側がフィールドを設定し終えた次フレームに発火する。
        IEnumerator DeferredActivate()
        {
            yield return null;
            if (_released) yield break;

            if (HideVisual)
            {
                _sr.sprite  = RuntimeSprite.Square();
                _sr.color   = new Color(1f, 0.35f, 0f, 0.55f);
                _sr.enabled = false; // LateUpdate()で毎フレーム切り替え
                FitColliderAndVisualToWorldSize(_sr);
            }
            else if (EffectSprite != null)
            {
                _sr.sprite = EffectSprite;
                _sr.color  = Color.white;
                _sr.flipX  = FlipEffectX;
                FitColliderAndVisualToWorldSize(_sr);
                _sr.enabled = true;
            }
            else
            {
                // 画像生成失敗時のフォールバック。四角ではなく属性色のエネルギー弾で表示する。
                _sr.sprite = RuntimeSprite.Glow();
                _sr.color  = SkillEnumParser.ElementColor(Element);
                FitColliderAndVisualToWorldSize(_sr);
                _sr.enabled = true;
            }

            _spawnTime = Time.time;
            if (GravityScale > 0f) _rb.gravityScale = GravityScale;
            _rb.linearVelocity = Direction * Speed;
            _activated = true;

            yield return new WaitForSeconds(Lifetime);
            Release();
        }

        void Update()
        {
            if (!_activated || _released) return;

            // ブーメラン: 寿命の半分で折り返す
            if (IsBoomerang && !_boomerangFlipped && Time.time - _spawnTime >= Lifetime * 0.5f)
            {
                if (_rb != null) { _rb.linearVelocity = -_rb.linearVelocity; Direction = -Direction; }
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
                    Release();
            }

            // 追尾: 毎フレーム速度を目標方向へ曲げる
            if (HomingTarget != null && HomingStrength > 0f && _rb != null)
            {
                Vector2 vel = _rb.linearVelocity;
                if (vel.sqrMagnitude > 0.01f)
                {
                    Vector2 toTarget = (Vector2)HomingTarget.position + Vector2.up * 0.8f - (Vector2)transform.position;
                    float maxTurn = HomingStrength * 280f * Time.deltaTime;
                    float angle = Mathf.Clamp(Vector2.SignedAngle(vel, toTarget), -maxTurn, maxTurn);
                    _rb.linearVelocity = (Vector2)(Quaternion.Euler(0f, 0f, angle) * vel);
                }
            }
        }

        void LateUpdate()
        {
            if (_released || !_activated || _debugSr == null) return;
            bool show = DebugSettings.ShowHitboxes;
            _debugSr.enabled = show;
            if (show && _col != null)
            {
                var b = _col.bounds;
                _debugSr.transform.position   = b.center;
                _debugSr.transform.rotation   = Quaternion.identity;
                _debugSr.transform.localScale  = new Vector3(b.size.x, b.size.y, 1f);
            }
            if (!HideVisual && _sr != null)
                _sr.enabled = !show;
        }

        // プールへ返却する
        void Release()
        {
            if (_released) return;
            _released = true;
            _activated = false;
            StopAllCoroutines();
            if (_debugSr != null) _debugSr.enabled = false;
            if (_rb != null) _rb.linearVelocity = Vector2.zero;
            gameObject.SetActive(false);
            s_pool.Push(this);
        }

        void OnDestroy()
        {
            if (_debugSr != null) Destroy(_debugSr.gameObject);
        }

        void FitColliderAndVisualToWorldSize(SpriteRenderer sr)
        {
            if (_col == null || sr?.sprite == null) return;

            Vector2 spriteSize = sr.sprite.bounds.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f) return;

            _col.size = spriteSize;
            _col.offset = Vector2.zero;
            transform.localScale = new Vector3(
                DesiredWorldSize.x / spriteSize.x,
                DesiredWorldSize.y / spriteSize.y,
                1f);
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (_cancelled || _released) return;

            // 飛び道具同士の相殺: 異なるオーナーの弾が衝突したら両方消滅
            var otherProj = other.GetComponent<Projectile>();
            if (otherProj != null && otherProj.Owner != Owner)
            {
                _cancelled = true;
                otherProj._cancelled = true;
                DamagePopup.SpawnText(transform.position, "相殺!", new Color(1f, 0.9f, 0.2f), 1.2f);
                otherProj.Release();
                Release();
                return;
            }

            // 音声アイテムへのヒット（中立物：陣営問わず誰でも殴れる。最後に削った人が取得者）
            var voiceItem = other.GetComponentInParent<Battle.VoiceItem>();
            if (voiceItem != null)
            {
                voiceItem.TakeHit(Damage, Owner);
                if (!IsBoomerang) Release();
                return;
            }

            // 召喚物へのヒット
            var summon = other.GetComponentInParent<Battle.SummonEntity>();
            if (summon != null && summon.Owner != Owner)
            {
                summon.TakeHit(Damage);
                if (!IsBoomerang) Release();
                return;
            }

            // 破壊可能な障害物（壁など）へのヒット
            var destructible = other.GetComponentInParent<Battle.DestructibleObstacle>();
            if (destructible != null)
            {
                destructible.TakeHit(Damage, Owner);
                if (!IsBoomerang) Release();
                return;
            }

            var target = other.GetComponentInParent<Fighter>();
            if (target == null)
            {
                // 壁・地面に当たった場合: ブーメランは貫通、通常弾は消える
                if (!IsBoomerang && other.gameObject.layer != 0) Release();
                return;
            }
            if (target == Owner)
            {
                if (IsBoomerang && _boomerangFlipped) Release(); // 回収
                return;
            }
            // フレンドリーファイアOFF：同陣営には当てない（Hitbox/SummonEntityと同じ扱い）
            if (Owner != null && target.Team == Owner.Team) return;
            if (target.IsDodging) return;

            // リフレクター: 速度・威力を1.2倍にして逆ベクトルで反射、オーナーを切り替え（1回限り）
            if (!_wasReflected && target.IsReflecting)
            {
                if (_rb != null) _rb.linearVelocity = -_rb.linearVelocity * 1.2f;
                Direction  = -Direction;
                Speed     *= 1.2f;
                Damage    *= 1.2f;
                Knockback *= 1.2f;
                Owner = target;
                _wasReflected = true;
                _boomerangHitSet?.Clear();
                DamagePopup.SpawnText(target.transform.position + Vector3.up * 0.5f, "REFLECT!", new Color(1f, 0.3f, 0.95f), 1.5f);
                PromptFighters.Battle.SimpleFX.ReflectFlash(transform.position);
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
                Release();
            }
        }
    }
}
