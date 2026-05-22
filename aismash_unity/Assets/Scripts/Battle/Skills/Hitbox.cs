using UnityEngine;
using System.Collections.Generic;

namespace PromptFighters.Battle.Skills
{
    // 一定時間だけ存在する近接攻撃判定。SkillExecutorが生成・破棄する。
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

        readonly HashSet<Fighter> _hitTargets = new HashSet<Fighter>();
        readonly Dictionary<Fighter, float> _nextHitTimes = new Dictionary<Fighter, float>();
        int _hitsLanded;

        // デバッグオーバーレイ（col.boundsに毎フレーム追従する独立オブジェクト）
        SpriteRenderer _debugSr;

        public static Hitbox Spawn(Fighter owner, Vector2 worldPos, Vector2 size, float lifetime)
        {
            var go = new GameObject("Hitbox");
            go.transform.position = worldPos;
            var col = go.AddComponent<BoxCollider2D>(); // 明示的追加（RequireComponent削除済み）
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

            // デバッグオーバーレイ作成
            var dbGo = new GameObject("HitboxDebug");
            var dbSr = dbGo.AddComponent<SpriteRenderer>();
            dbSr.sprite       = RuntimeSprite.Square();
            dbSr.color        = new Color(1f, 0.35f, 0f, 0.6f); // 橙: 攻撃判定
            dbSr.sortingOrder = 12;
            dbSr.enabled      = false;
            hb._debugSr = dbSr;

            return hb;
        }

        public void SetDebugColor(Color c)
        {
            if (_debugSr != null) _debugSr.color = c;
        }

        // ring形状用: CircleCollider2Dで生成する
        public static Hitbox SpawnCircle(Fighter owner, Vector2 worldPos, float radius, float lifetime)
        {
            var go = new GameObject("HitboxRing");
            go.transform.position = worldPos;

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius    = radius;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprite.Square();
            sr.color  = new Color(1f, 1f, 0f, 0f); // 不可視（ring は常にHideVisual扱い）
            sr.sortingOrder = 10;
            go.transform.localScale = Vector3.one;

            var hb = go.AddComponent<Hitbox>();
            hb.Owner      = owner;
            hb.Lifetime   = lifetime;
            hb.HideVisual = true;

            var dbGo = new GameObject("HitboxDebug");
            var dbSr = dbGo.AddComponent<SpriteRenderer>();
            dbSr.sprite       = RuntimeSprite.Circle(); // 円スプライトで実形状を表示
            dbSr.color        = new Color(0.3f, 1f, 0.3f, 0.6f); // 緑: リング判定
            dbSr.sortingOrder = 12;
            dbSr.enabled      = false;
            hb._debugSr = dbSr;

            return hb;
        }

        void Start()
        {
            Color ec = SkillEnumParser.ElementColor(Element);
            var sr = GetComponent<SpriteRenderer>();
            if (HideVisual)
            {
                sr.enabled = false;
            }
            else if (EffectSprite != null)
            {
                sr.sprite = EffectSprite;
                sr.color  = Color.white;
                sr.flipX  = FlipEffectX;
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
            if (show)
            {
                var col2d = GetComponent<Collider2D>();
                if (col2d != null)
                {
                    var b = col2d.bounds;
                    _debugSr.transform.position   = b.center;
                    _debugSr.transform.rotation   = Quaternion.identity;
                    _debugSr.transform.localScale = new Vector3(b.size.x, b.size.y, 1f);
                }
            }

            // デバッグ中はエフェクトスプライトを非表示にしてブロックのみ見せる
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
                Battle.BattleManager.Instance?.TriggerHitStop(0.20f, 0.05f);

            if (Status != StatusType.None && Random.value <= StatusChance)
                target.ApplyStatus(Status, StatusDuration);
        }
    }
}
