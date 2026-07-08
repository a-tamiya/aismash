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

        // 拡張バリエーション
        public float ExplosionRadius; // >0: 着弾・壁・寿命切れで爆発（範囲ダメージ。直撃は爆発に一本化）
        public int   BounceCount;     // >0: 地面・壁で跳ね返る回数（跳弾）
        public float WaveAmplitude;   // >0: 進行方向と垂直にうねりながら飛ぶ（波状弾）
        public bool  Pierce;          // true: 敵を貫通する（1体につき1ヒット）

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
            ExplosionRadius = 0f;
            BounceCount = 0;
            WaveAmplitude = 0f;
            Pierce = false;

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
            // 爆発弾は寿命切れでもその場で爆発する（時限爆弾的な使い方ができる）
            if (ExplosionRadius > 0f && !_cancelled) Explode();
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

            // 波状弾: 進行方向と垂直にうねる（追尾・重力・ブーメランとは併用しない）
            if (WaveAmplitude > 0f && HomingTarget == null && GravityScale <= 0f && !IsBoomerang && _rb != null)
            {
                const float freq = 7f;
                Vector2 perp = new Vector2(-Direction.y, Direction.x);
                _rb.linearVelocity = Direction * Speed
                    + perp * (WaveAmplitude * freq * Mathf.Cos((Time.time - _spawnTime) * freq));
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

        // 爆発弾: 現在位置に円形の範囲判定＋属性色の爆発フラッシュを出す。
        void Explode()
        {
            Vector2 pos = transform.position;
            var hb = Hitbox.SpawnCircle(Owner, pos, ExplosionRadius, 0.12f);
            hb.Damage                   = Damage;
            hb.DamageIncludesOwnerBoost = DamageIncludesOwnerBoost;
            hb.Knockback                = Knockback;
            hb.KnockbackDir             = new Vector2(Mathf.Abs(KnockbackDir.x), Mathf.Max(KnockbackDir.y, 0.5f));
            hb.StunTime                 = StunTime;
            hb.GuardDamage              = GuardDamage;
            hb.Element                  = Element;
            hb.Status                   = Status;
            hb.StatusDuration           = StatusDuration;
            hb.StatusChance             = StatusChance;
            hb.MaxHits                  = 2;

            ExplosionFlash.Spawn(pos, ExplosionRadius * 2f, SkillEnumParser.ElementColor(Element));
            Battle.CameraShake.Shake(0.12f, 0.16f);
        }

        // 跳弾: 地面なら上方向へ、壁なら左右反転して跳ね返る。
        void DoBounce(Collider2D surface)
        {
            BounceCount--;
            if (_rb == null) return;
            Vector2 v = _rb.linearVelocity;
            bool floorLike = transform.position.y > surface.bounds.max.y - 0.25f;
            if (floorLike)
            {
                float vy = Mathf.Max(Mathf.Abs(v.y) * 0.9f, Speed * 0.4f);
                _rb.linearVelocity = new Vector2(v.x, vy);
            }
            else
            {
                _rb.linearVelocity = new Vector2(-v.x, v.y);
                Direction = new Vector2(-Direction.x, Direction.y);
                if (_sr != null) _sr.flipX = !_sr.flipX;
            }
        }

        // 爆発の見た目: 属性色のグローが広がりながら消える（自己完結の使い捨てオブジェクト）。
        class ExplosionFlash : MonoBehaviour
        {
            SpriteRenderer _sr;
            float _t;
            float _diameter;
            Color _color;
            const float Dur = 0.22f;

            public static void Spawn(Vector2 pos, float diameter, Color color)
            {
                var go = new GameObject("ExplosionFlash");
                go.transform.position = pos;
                var fx = go.AddComponent<ExplosionFlash>();
                fx._diameter = diameter;
                fx._color = color;
                fx._sr = go.AddComponent<SpriteRenderer>();
                fx._sr.sprite = RuntimeSprite.Glow();
                fx._sr.sortingOrder = 11;
            }

            void Update()
            {
                _t += Time.deltaTime;
                float k = Mathf.Clamp01(_t / Dur);
                float d = _diameter * Mathf.Lerp(0.4f, 1f, 1f - (1f - k) * (1f - k));
                Vector2 ss = _sr.sprite.bounds.size;
                if (ss.x > 0f && ss.y > 0f)
                    transform.localScale = new Vector3(d / ss.x, d / ss.y, 1f);
                _sr.color = new Color(_color.r, _color.g, _color.b, Mathf.Lerp(0.9f, 0f, k));
                if (_t >= Dur) Destroy(gameObject);
            }
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
                // 壁・地面に当たった場合: ブーメランは貫通、跳弾は反射、爆発弾は爆発、通常弾は消える。
                // 跳弾は「地面レイヤー」ならレイヤー番号に関わらず必ず跳ねる
                // （ステージによって床がDefaultレイヤーでも取りこぼさない）。
                bool groundish = Owner != null &&
                    (Owner.groundLayer.value & (1 << other.gameObject.layer)) != 0;
                if (!IsBoomerang && BounceCount > 0 && (groundish || other.gameObject.layer != 0))
                {
                    DoBounce(other);
                    return;
                }
                if (!IsBoomerang && other.gameObject.layer != 0)
                {
                    if (ExplosionRadius > 0f) Explode();
                    Release();
                }
                return;
            }
            if (target == Owner)
            {
                if (IsBoomerang && _boomerangFlipped) Release(); // 回収
                return;
            }
            // フレンドリーファイアOFF：同陣営には当てない（Hitbox/SummonEntityと同じ扱い）。
            // チュートリアルの練習台は陣営に関わらずどちらからも攻撃が通る。
            if (Owner != null && target.Team == Owner.Team && !target.IsPracticeDummy) return;
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

            // 爆発弾: 直撃時はダメージを爆発（範囲判定）に一本化する（直撃＋爆発の二重ヒット防止）
            if (ExplosionRadius > 0f && !IsBoomerang)
            {
                Explode();
                Release();
                return;
            }

            float dir = FixedKnockbackDir ? 1f : Mathf.Sign(Direction.x);
            if (dir == 0f) dir = 1f;
            var kb = new Vector2(dir * KnockbackDir.x * Knockback, KnockbackDir.y * Knockback);

            // 貫通弾: 当たっても消えず、1体につき1回だけヒットする
            if (Pierce && !IsBoomerang)
            {
                if (_boomerangHitSet == null) _boomerangHitSet = new HashSet<Fighter>();
                if (_boomerangHitSet.Contains(target)) return;
                _boomerangHitSet.Add(target);
                target.TakeDamage(Damage, Knockback, kb, StunTime, GuardDamage, !DamageIncludesOwnerBoost);
                if (GroundBounce) target.StartGroundBounce(Knockback * 0.75f);
                if (Status != StatusType.None && Random.value <= StatusChance)
                    target.ApplyStatus(Status, StatusDuration);
                return;
            }

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
