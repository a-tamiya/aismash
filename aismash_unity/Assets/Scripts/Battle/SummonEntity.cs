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
        const int MaxPerOwner = 3; // 同一オーナーの同時召喚上限（無限展開の防止）
        float _hp = MaxHP;

        Rigidbody2D _rb;
        SpriteRenderer _sr;
        float _startX;
        float _startY;
        float _dir = 1f;
        float _vertDir = 1f; // diagonal/hover用の垂直方向
        const float VerticalRange = 1.3f;      // 上下方向の振れ幅
        const float VerticalSpeedFactor = 0.7f; // 横速度に対する縦方向速度の比率
        float _lifetime = 3f;
        float _age;
        float _flashTimer;   // 被弾フラッシュの残り時間
        Vector3 _baseScale = Vector3.one;
        Color   _baseColor = Color.white;
        readonly HashSet<Fighter> _recentHits = new HashSet<Fighter>();

        // 生存中の全召喚体（オーナーごとの数の上限管理用）
        static readonly List<SummonEntity> s_active = new List<SummonEntity>();

        public static SummonEntity Spawn(Fighter owner, Vector2 pos, float speed, float lifetime,
                                         float damage, float knockback, Element element,
                                         Sprite sprite = null, Vector2? desiredWorldSize = null,
                                         SkillAction sourceAction = null)
        {
            var go = new GameObject("SummonEntity");
            go.transform.position = pos;
            go.layer = owner.gameObject.layer;

            // 追尾・斜め・上下移動は縦方向にも動く必要があるため、その場合だけY固定を外す。
            bool needsVerticalMotion = sourceAction != null && (
                sourceAction.homing ||
                sourceAction.direction == "diagonal" ||
                sourceAction.direction == "hover");

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.constraints  = needsVerticalMotion
                ? RigidbodyConstraints2D.FreezeRotation
                : RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionY;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite != null ? sprite : RuntimeSprite.Glow();
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
            s._lifetime  = Mathf.Max(0.3f, lifetime);
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

            // 足元の影（接地感）
            float groundY = BattleManager.Instance != null ? BattleManager.Instance.StageGroundY : pos.y - 1f;
            BlobShadow.Spawn(go.transform, groundY, Mathf.Max(0.6f, worldSize.x * 1.1f), sortingOrder: -2);

            // 同一オーナーの召喚は最大数まで。超えたら最も古い個体から静かに消す。
            s_active.RemoveAll(e => e == null);
            s_active.Add(s);
            int owned = 0;
            for (int i = s_active.Count - 1; i >= 0; i--)
            {
                if (s_active[i].Owner != owner) continue;
                owned++;
                if (owned > MaxPerOwner) Object.Destroy(s_active[i].gameObject);
            }

            return s;
        }

        void Start()
        {
            _rb        = GetComponent<Rigidbody2D>();
            _sr        = GetComponent<SpriteRenderer>();
            _startX    = transform.position.x;
            _startY    = transform.position.y;
            _dir       = InitialDirection();
            _baseScale = transform.localScale;
            if (_sr != null) _baseColor = _sr.color;
            _rb.linearVelocity = new Vector2(_dir * Speed, 0f);
        }

        void OnDestroy()
        {
            s_active.Remove(this);
        }

        void Update()
        {
            // 寿命・演出（出現ポップ／消滅フェード／被弾フラッシュ／低耐久点滅／生き物らしい脈動）
            _age += Time.deltaTime;
            if (_flashTimer > 0f) _flashTimer -= Time.deltaTime;
            UpdateVisual();
            if (_age >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }

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

            if (Homing && Owner != null)
            {
                // ボイスボールが出ている間はそちらを優先して追尾する（相手より先に割って割り込む）。
                Transform homingTarget = VoiceItem.Active != null ? VoiceItem.Active.transform
                    : Owner.Opponent != null ? Owner.Opponent.transform : null;
                if (homingTarget != null)
                {
                    float dx = homingTarget.position.x - transform.position.x;
                    // 相手と重なる付近でdxの符号が細かくぶれて左右反転を連発しないよう、
                    // 現在の向きと逆側へ切り替えるときだけ広めのしきい値を要求する（ヒステリシス）。
                    bool sameDir  = (dx >= 0f) == (_dir >= 0f);
                    float threshold = sameDir ? 0.05f : 0.35f;
                    if (Mathf.Abs(dx) > threshold) _dir = Mathf.Sign(dx);

                    // 縦方向も追尾する（比例制御。フリップは絡まないのでヒステリシス不要）。
                    float dy = homingTarget.position.y - transform.position.y;
                    float vy = Mathf.Clamp(dy * 2.5f, -Speed, Speed);
                    _rb.linearVelocity = new Vector2(_dir * Speed, vy);
                    GetComponent<SpriteRenderer>().flipX = _dir < 0;
                    return;
                }
            }

            if (Direction == "stationary")
            {
                _rb.linearVelocity = Vector2.zero;
                return;
            }

            // 上下にホバリングしながら緩やかに横移動する（幽霊・浮遊物向け）。
            if (Direction == "hover")
            {
                float targetY = _startY + Mathf.Sin(_age * 2.2f) * VerticalRange;
                float vy = (targetY - transform.position.y) * 6f;
                float distXHover = transform.position.x - _startX;
                if ((_dir > 0 && distXHover > PatrolRange) || (_dir < 0 && distXHover < -PatrolRange))
                {
                    _dir = -_dir;
                    GetComponent<SpriteRenderer>().flipX = _dir < 0;
                }
                _rb.linearVelocity = new Vector2(_dir * Speed * 0.4f, vy);
                return;
            }

            // 斜めに往復する（左右のパトロールに上下の往復を組み合わせる）。
            if (Direction == "diagonal")
            {
                float distXDiag = transform.position.x - _startX;
                if ((_dir > 0 && distXDiag > PatrolRange) || (_dir < 0 && distXDiag < -PatrolRange))
                {
                    _dir = -_dir;
                    GetComponent<SpriteRenderer>().flipX = _dir < 0;
                }
                float distYDiag = transform.position.y - _startY;
                if ((_vertDir > 0 && distYDiag > VerticalRange) || (_vertDir < 0 && distYDiag < -VerticalRange))
                    _vertDir = -_vertDir;
                _rb.linearVelocity = new Vector2(_dir * Speed, _vertDir * Speed * VerticalSpeedFactor);
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

        // 出現ポップ・消滅フェード・被弾フラッシュ・低耐久点滅・脈動をまとめて適用する。
        void UpdateVisual()
        {
            if (_sr == null) return;

            // 出現ポップ（小さく生まれて弾みながら等倍へ）
            float popT = Mathf.Clamp01(_age / 0.18f);
            float pop  = Mathf.Lerp(0.25f, 1f, 1f - (1f - popT) * (1f - popT));

            // 生き物らしいスクワッシュ＆ストレッチ（面積ほぼ一定の2D変形）
            float wob = 0.04f * Mathf.Sin((_age + GetInstanceID() * 0.13f) * 7f);

            // 寿命間際は縮みながらフェードアウト
            float outT   = Mathf.Clamp01((_lifetime - _age) / 0.35f);
            float shrink = Mathf.Lerp(0.6f, 1f, outT);

            float k = pop * shrink;
            transform.localScale = new Vector3(
                _baseScale.x * (1f + wob) * k,
                _baseScale.y * (1f - wob) * k, 1f);

            Color c = _baseColor;
            if (_flashTimer > 0f)
                c = Color.Lerp(c, new Color(1f, 0.42f, 0.36f), Mathf.Clamp01(_flashTimer / 0.15f));
            if (_hp <= MaxHP * 0.3f)
                c.a *= 0.72f + 0.28f * Mathf.Sin(Time.time * 10f); // 破壊寸前の点滅
            c.a *= outT;
            _sr.color = c;
        }

        public void TakeHit(float dmg)
        {
            if (dmg <= 0f) return;
            _hp -= dmg;
            _flashTimer = 0.15f;
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
            // ボイスボールへも攻撃を通す（陣営問わず誰でも殴れる中立物）。
            var voiceItem = other.GetComponentInParent<VoiceItem>();
            if (voiceItem != null)
            {
                voiceItem.TakeHit(Damage, Owner);
                return;
            }

            var target = other.GetComponentInParent<Fighter>();
            if (target == null || target == Owner) return;
            // フレンドリーファイアOFF：同陣営には当てない。
            // チュートリアルの練習台は陣営に関わらずどちらからも攻撃が通る。
            if (Owner != null && target.Team == Owner.Team && !target.IsPracticeDummy) return;
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
