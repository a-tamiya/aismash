using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace PromptFighters.Battle.Skills
{
    // 一定時間だけ存在する近接攻撃判定。SkillExecutorが生成する。
    // GC負荷軽減のためオブジェクトプールで再利用する（box用/circle用で別プール）。
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
        public bool         FixedKnockbackDir; // trueのとき KnockbackDir.x の符号をそのまま使う
        public bool         GroundBounce;     // ヒット時に地面バウンドさせる
        public bool         IsSmashHit;       // 最大チャージスマッシュヒット時のスロー演出用
        public float        LifestealRatio;   // ヒット時に与ダメージ×この割合だけ owner を回復

        readonly HashSet<Fighter> _hitTargets = new HashSet<Fighter>();
        readonly Dictionary<Fighter, float> _nextHitTimes = new Dictionary<Fighter, float>();
        readonly HashSet<Battle.SummonEntity> _hitSummons = new HashSet<Battle.SummonEntity>();
        readonly HashSet<Battle.VoiceItem> _hitVoiceItems = new HashSet<Battle.VoiceItem>();
        readonly HashSet<Battle.DestructibleObstacle> _hitDestructibles = new HashSet<Battle.DestructibleObstacle>();
        int _hitsLanded;

        // デバッグオーバーレイ（col.boundsに毎フレーム追従する独立オブジェクト。プール対象と一緒に再利用）
        SpriteRenderer _debugSr;

        bool _isCircle;
        bool _released;
        bool _activated;
        SpriteRenderer _sr;
        Collider2D _col;

        static readonly Stack<Hitbox> s_boxPool    = new Stack<Hitbox>();
        static readonly Stack<Hitbox> s_circlePool = new Stack<Hitbox>();

        public static Hitbox Spawn(Fighter owner, Vector2 worldPos, Vector2 size, float lifetime)
        {
            var hb = Acquire(circle: false);
            hb.transform.position = worldPos;
            hb.transform.localScale = new Vector3(size.x, size.y, 1f);
            var box = (BoxCollider2D)hb._col;
            box.size = Vector2.one; // スケールで大きさを制御するためcolliderは1x1

            hb._sr.sprite       = RuntimeSprite.Square();
            hb._sr.color        = new Color(1f, 1f, 0f, 0.55f);
            hb._sr.enabled      = false; // アクティベート完了まで描画しない（1フレーム点滅防止）

            hb.Owner    = owner;
            hb.Lifetime = lifetime;
            hb.DesiredWorldSize = size;
            hb.BeginDeferredActivate();
            return hb;
        }

        // ring形状用: CircleCollider2Dで生成する
        public static Hitbox SpawnCircle(Fighter owner, Vector2 worldPos, float radius, float lifetime)
        {
            var hb = Acquire(circle: true);
            hb.transform.position = worldPos;
            hb.transform.localScale = Vector3.one;
            var circle = (CircleCollider2D)hb._col;
            circle.radius = radius;

            hb._sr.sprite = RuntimeSprite.Square();
            hb._sr.color  = new Color(1f, 1f, 0f, 0f); // 不可視（ring は常にHideVisual扱い）
            hb._sr.enabled = false;

            hb.Owner      = owner;
            hb.Lifetime   = lifetime;
            hb.HideVisual = true;
            hb.BeginDeferredActivate();
            return hb;
        }

        static Hitbox Acquire(bool circle)
        {
            var pool = circle ? s_circlePool : s_boxPool;
            Hitbox hb = null;
            while (pool.Count > 0)
            {
                hb = pool.Pop();
                if (hb != null) break; // 破棄済み（シーン遷移等）はスキップ
            }
            if (hb == null) hb = Create(circle);
            hb.ResetState();
            hb.gameObject.SetActive(true);
            return hb;
        }

        static Hitbox Create(bool circle)
        {
            var go = new GameObject(circle ? "HitboxRing" : "Hitbox");
            Collider2D col;
            if (circle)
            {
                var c = go.AddComponent<CircleCollider2D>();
                c.isTrigger = true;
                col = c;
            }
            else
            {
                var b = go.AddComponent<BoxCollider2D>();
                b.isTrigger = true;
                b.size      = Vector2.one;
                col = b;
            }

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = RuntimeSprite.Square();
            sr.sortingOrder = 10;

            var hb = go.AddComponent<Hitbox>();
            hb._isCircle = circle;
            hb._sr  = sr;
            hb._col = col;

            // デバッグオーバーレイ（独立オブジェクト。プール対象と寿命を共有して再利用）
            var dbGo = new GameObject("HitboxDebug");
            var dbSr = dbGo.AddComponent<SpriteRenderer>();
            dbSr.sprite       = circle ? RuntimeSprite.Circle() : RuntimeSprite.Square();
            dbSr.color        = circle ? new Color(0.3f, 1f, 0.3f, 0.6f) : new Color(1f, 0.35f, 0f, 0.6f);
            dbSr.sortingOrder = 12;
            dbSr.enabled      = false;
            hb._debugSr = dbSr;

            return hb;
        }

        // 再利用前に全状態を初期化する
        void ResetState()
        {
            _released = false;
            _activated = false;
            _hitTargets.Clear();
            _nextHitTimes.Clear();
            _hitSummons.Clear();
            _hitVoiceItems.Clear();
            _hitDestructibles.Clear();
            _hitsLanded = 0;

            Owner = null;
            Damage = 0f;
            Knockback = 0f;
            KnockbackDir = Vector2.right;
            StunTime = 0f;
            GuardDamage = 0f;
            Status = StatusType.None;
            StatusDuration = 0f;
            StatusChance = 1f;
            Element = Element.None;
            EffectSprite = null;
            FlipEffectX = false;
            MaxHits = 1;
            Lifetime = 0.1f;
            FollowOwner = false;
            HideVisual = false;
            DamageIncludesOwnerBoost = false;
            OwnerLocalOffset = Vector2.zero;
            DesiredWorldSize = Vector2.zero;
            FixedKnockbackDir = false;
            GroundBounce = false;
            IsSmashHit = false;
            LifestealRatio = 0f;

            transform.rotation = Quaternion.identity;
            if (_col != null) _col.enabled = true;
            if (!_isCircle && _col is BoxCollider2D box) { box.size = Vector2.one; box.offset = Vector2.zero; }
        }

        public void SetDebugColor(Color c)
        {
            if (_debugSr != null) _debugSr.color = c;
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

            Color ec = SkillEnumParser.ElementColor(Element);
            if (HideVisual)
            {
                _sr.enabled = false;
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
                // 画像生成失敗時のフォールバック。四角ではなく属性色のエネルギー塊で表示する。
                _sr.sprite  = RuntimeSprite.Glow();
                _sr.color   = new Color(ec.r, ec.g, ec.b, 0.85f);
                _sr.flipX   = false;
                _sr.enabled = true;
            }

            _activated = true;
            yield return new WaitForSeconds(Lifetime);
            Release();
        }

        void LateUpdate()
        {
            if (_released || !_activated) return;

            // FollowOwner処理
            if (FollowOwner && Owner != null)
            {
                float dirSign = Owner.FacingRight ? 1f : -1f;
                transform.position = (Vector2)Owner.transform.position +
                    new Vector2(dirSign * OwnerLocalOffset.x, OwnerLocalOffset.y);
            }

            // デバッグオーバーレイをcol.boundsに追従
            if (_debugSr == null) return;
            bool show = DebugSettings.ShowHitboxes;
            _debugSr.enabled = show;
            if (show && _col != null)
            {
                var b = _col.bounds;
                _debugSr.transform.position   = b.center;
                _debugSr.transform.rotation   = Quaternion.identity;
                _debugSr.transform.localScale = new Vector3(b.size.x, b.size.y, 1f);
            }

            // デバッグ中はエフェクトスプライトを非表示にしてブロックのみ見せる
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
            gameObject.SetActive(false);
            (_isCircle ? s_circlePool : s_boxPool).Push(this);
        }

        void OnDestroy()
        {
            if (_debugSr != null) Destroy(_debugSr.gameObject);
        }

        void FitColliderAndVisualToWorldSize(SpriteRenderer sr)
        {
            var col = _col as BoxCollider2D;
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
            if (_released) return;
            if (_hitsLanded >= MaxHits) return;

            // 音声アイテムへのヒット（中立物：陣営問わず誰でも殴れる。最後に削った人が取得者）
            var voiceItem = other.GetComponentInParent<Battle.VoiceItem>();
            if (voiceItem != null && !_hitVoiceItems.Contains(voiceItem))
            {
                _hitVoiceItems.Add(voiceItem);
                voiceItem.TakeHit(Damage, Owner);
                _hitsLanded++;
                if (_hitsLanded >= MaxHits && _col != null)
                    _col.enabled = false;
                return;
            }

            // 召喚物へのヒット
            var summon = other.GetComponentInParent<Battle.SummonEntity>();
            if (summon != null && summon.Owner != Owner && !_hitSummons.Contains(summon))
            {
                _hitSummons.Add(summon);
                summon.TakeHit(Damage);
                _hitsLanded++;
                if (_hitsLanded >= MaxHits && _col != null)
                    _col.enabled = false;
                return;
            }

            var target = other.GetComponentInParent<Fighter>();
            if (target == null || target == Owner) return;
            // フレンドリーファイアOFF：同陣営には当てない（1v1はfighter1=Players/fighter2=Enemiesで別陣営）
            if (Owner != null && target.Team == Owner.Team) return;
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
            if (_hitsLanded >= MaxHits && _col != null)
            {
                // コライダーを無効化してビジュアルは lifetime まで表示し続ける
                _col.enabled = false;
            }
        }

        void ApplyHit(Fighter target)
        {
            float dir;
            if (FixedKnockbackDir)
                dir = 1f;
            else
            {
                dir = Mathf.Sign(target.transform.position.x - (Owner != null ? Owner.transform.position.x : transform.position.x));
                if (dir == 0f) dir = 1f;
            }
            var kb = new Vector2(dir * KnockbackDir.x, KnockbackDir.y);

            target.TakeDamage(Damage, Knockback, kb, StunTime, GuardDamage, !DamageIncludesOwnerBoost);
            if (GroundBounce) target.StartGroundBounce(Knockback * 0.75f);

            if (IsSmashHit)
            {
                Battle.BattleManager.Instance?.TriggerHitStop(0.20f, 0.05f);
                Battle.CameraShake.Shake(0.38f, 0.32f);
            }

            if (Status != StatusType.None && Random.value <= StatusChance)
                target.ApplyStatus(Status, StatusDuration);

            if (LifestealRatio > 0f && Owner != null)
                Owner.Heal(Damage * LifestealRatio);
        }
    }
}
