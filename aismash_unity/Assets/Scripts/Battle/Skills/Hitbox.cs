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
        public string       SpatialKnockbackMode;
        public Vector2      SpatialKnockbackOrigin;
        public bool         GroundBounce;     // ヒット時に地面バウンドさせる
        public bool         IsSmashHit;       // 最大チャージスマッシュヒット時のスロー演出用
        public float        LifestealRatio;   // ヒット時に与ダメージ×この割合だけ owner を回復
        public bool         IsTrap;           // 設置技。アーム時間・待機脈動・触発時の爆発演出を付ける
        public float        ArmTime;          // 設置からアーム完了（判定有効化）までの秒数

        // SkillAction.pattern で展開された兄弟判定の重複ヒット防止。
        public int          SharedCastId;
        public int          SharedSourceId;
        public float        SharedHitLockSeconds = 0.08f;

        // broad-phase Collider の内側で追加判定する空間形状。
        // box/line/column はCollider自体が正確な形状。annulus/arc/crossは下記パラメータで絞り込む。
        public string       SpatialShape;
        public float        SpatialInnerRadius;
        public float        SpatialArcAngle = 360f;
        public float        SpatialCrossThickness;

        Vector3 _visualBaseScale = Vector3.one;
        bool    _exhausted;

        readonly HashSet<Fighter> _hitTargets = new HashSet<Fighter>();
        readonly Dictionary<Fighter, float> _nextHitTimes = new Dictionary<Fighter, float>();
        readonly HashSet<Battle.SummonEntity> _hitSummons = new HashSet<Battle.SummonEntity>();
        readonly HashSet<Battle.VoiceItem> _hitVoiceItems = new HashSet<Battle.VoiceItem>();
        readonly HashSet<Battle.DestructibleObstacle> _hitDestructibles = new HashSet<Battle.DestructibleObstacle>();
        int _hitsLanded;

        // デバッグオーバーレイ（col.boundsに毎フレーム追従する独立オブジェクト。プール対象と一緒に再利用）
        SpriteRenderer _debugSr;
        LineRenderer _shapeOutlineA;
        LineRenderer _shapeOutlineB;
        static Material s_shapeLineMaterial;

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

            // 静的な障害物（壁など）はStatic判定なので、無RBのトリガー同士では
            // トリガーイベントが発火しない。Kinematic RBを持たせて検出できるようにする。
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType    = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;

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
            SpatialKnockbackMode = null;
            SpatialKnockbackOrigin = Vector2.zero;
            GroundBounce = false;
            IsSmashHit = false;
            LifestealRatio = 0f;
            IsTrap = false;
            ArmTime = 0f;
            SharedCastId = 0;
            SharedSourceId = 0;
            SharedHitLockSeconds = 0.08f;
            SpatialShape = null;
            SpatialInnerRadius = 0f;
            SpatialArcAngle = 360f;
            SpatialCrossThickness = 0f;
            _exhausted = false;
            _visualBaseScale = Vector3.one;
            if (_shapeOutlineA != null) _shapeOutlineA.enabled = false;
            if (_shapeOutlineB != null) _shapeOutlineB.enabled = false;
            if (_debugSr != null)
                _debugSr.color = _isCircle
                    ? new Color(0.3f, 1f, 0.3f, 0.6f)
                    : new Color(1f, 0.35f, 0f, 0.6f);

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
            _visualBaseScale = transform.localScale;
            UpdateSpatialOutline();
            if (UsesProceduralFallbackVisual() && _sr != null) _sr.enabled = false;

            // 設置技: アーム時間中は判定を無効化（密着で即当たる置き逃げを防ぎ、「設置した」感を出す）
            float armed = 0f;
            if (IsTrap && ArmTime > 0f && _col != null)
            {
                _col.enabled = false;
                armed = Mathf.Min(ArmTime, Lifetime * 0.5f);
                yield return new WaitForSeconds(armed);
                if (_released) yield break;
                if (_col != null && _hitsLanded < MaxHits) _col.enabled = true;
            }

            yield return new WaitForSeconds(Mathf.Max(0.01f, Lifetime - armed));
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

            // 設置技の待機脈動（罠が「そこにある」ことを両プレイヤーに読ませる）
            if (IsTrap && !_exhausted && !HideVisual && _sr != null && _sr.enabled)
            {
                float p = Mathf.Sin(Time.time * 6f) * 0.5f + 0.5f;
                transform.localScale = _visualBaseScale * (1f + 0.06f * p);
                var c = _sr.color;
                c.a = Mathf.Lerp(0.72f, 1f, p);
                _sr.color = c;
            }

            UpdateSpatialOutline();

            // デバッグオーバーレイをcol.boundsに追従
            if (_debugSr == null) return;
            bool show = DebugSettings.ShowHitboxes;
            _debugSr.enabled = show;
            if (_shapeOutlineA != null)
                _shapeOutlineA.enabled = !show && !HideVisual && UsesProceduralFallbackVisual();
            if (_shapeOutlineB != null) _shapeOutlineB.enabled = !show && !HideVisual &&
                UsesProceduralFallbackVisual() && SpatialShape == "annulus" && SpatialInnerRadius > 0f;
            if (show && _col != null)
            {
                Vector2 size = DesiredWorldSize;
                if (size.x <= 0f || size.y <= 0f)
                    size = _col.bounds.size;
                _debugSr.transform.position   = transform.position;
                _debugSr.transform.rotation   = transform.rotation;
                _debugSr.transform.localScale = new Vector3(size.x, size.y, 1f);
            }

            // デバッグ中はエフェクトスプライトを非表示にしてブロックのみ見せる
            if (!HideVisual && _sr != null)
                _sr.enabled = !show && !UsesProceduralFallbackVisual();
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
            if (_shapeOutlineA != null) Destroy(_shapeOutlineA.gameObject);
            if (_shapeOutlineB != null) Destroy(_shapeOutlineB.gameObject);
        }

        // 生成済みの技エフェクト画像を最優先する。空間形状の輪郭は画像が無い場合だけ使い、
        // annulus/arc/cross/cone等を指定しただけで既存・生成済みPNGを置換しない。
        bool UsesProceduralFallbackVisual()
            => EffectSprite == null &&
               (SpatialShape == "annulus" || SpatialShape == "arc" || SpatialShape == "cross" ||
                SpatialShape == "cone" || SpatialShape == "line" || SpatialShape == "column");

        void UpdateSpatialOutline()
        {
            if (!UsesProceduralFallbackVisual() || HideVisual)
            {
                if (_shapeOutlineA != null) _shapeOutlineA.enabled = false;
                if (_shapeOutlineB != null) _shapeOutlineB.enabled = false;
                return;
            }

            EnsureShapeOutlines();
            Color color = SkillEnumParser.ElementColor(Element);
            color.a = 0.9f;
            ConfigureLine(_shapeOutlineA, color);
            ConfigureLine(_shapeOutlineB, color);

            float width = Mathf.Max(0.05f, DesiredWorldSize.x);
            float height = Mathf.Max(0.05f, DesiredWorldSize.y);
            switch (SpatialShape)
            {
                case "annulus":
                {
                    float outer = Mathf.Min(width, height) * 0.5f;
                    SetCircle(_shapeOutlineA, outer);
                    if (SpatialInnerRadius > 0.01f)
                    {
                        SetCircle(_shapeOutlineB, Mathf.Min(SpatialInnerRadius, outer - 0.02f));
                        _shapeOutlineB.enabled = true;
                    }
                    else _shapeOutlineB.enabled = false;
                    break;
                }
                case "arc":
                    SetArc(_shapeOutlineA, Mathf.Min(width, height) * 0.5f,
                        Mathf.Max(0f, SpatialInnerRadius), SpatialArcAngle);
                    _shapeOutlineB.enabled = false;
                    break;
                case "cross":
                    SetCross(_shapeOutlineA, width, height,
                        SpatialCrossThickness > 0f ? SpatialCrossThickness : Mathf.Min(width, height) * 0.3f);
                    _shapeOutlineB.enabled = false;
                    break;
                case "cone":
                    SetCone(_shapeOutlineA, width, height);
                    _shapeOutlineB.enabled = false;
                    break;
                default:
                    SetBox(_shapeOutlineA, width, height);
                    _shapeOutlineB.enabled = false;
                    break;
            }
        }

        void EnsureShapeOutlines()
        {
            if (s_shapeLineMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null) s_shapeLineMaterial = new Material(shader) { name = "SkillShapeOutline" };
            }
            if (_shapeOutlineA == null) _shapeOutlineA = CreateShapeOutline("ShapeOutlineA");
            if (_shapeOutlineB == null) _shapeOutlineB = CreateShapeOutline("ShapeOutlineB");
        }

        LineRenderer CreateShapeOutline(string objectName)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.sortingOrder = 11;
            line.numCapVertices = 2;
            if (s_shapeLineMaterial != null) line.sharedMaterial = s_shapeLineMaterial;
            return line;
        }

        static void ConfigureLine(LineRenderer line, Color color)
        {
            if (line == null) return;
            line.enabled = true;
            line.startWidth = 0.055f;
            line.endWidth = 0.055f;
            line.startColor = color;
            line.endColor = color;
        }

        Vector3 ShapePoint(float x, float y)
        {
            Vector3 localWorld = new Vector3(x, y, 0f);
            return transform.position + transform.rotation * localWorld;
        }

        void SetCircle(LineRenderer line, float radius)
        {
            const int segments = 40;
            line.loop = true;
            line.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, ShapePoint(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
            }
        }

        void SetArc(LineRenderer line, float outerRadius, float innerRadius, float arcDegrees)
        {
            const int segments = 24;
            float half = Mathf.Clamp(arcDegrees > 0f ? arcDegrees : 90f, 1f, 360f) * 0.5f;
            innerRadius = Mathf.Clamp(innerRadius, 0f, Mathf.Max(0f, outerRadius - 0.02f));
            int innerPoints = innerRadius > 0.01f ? segments + 1 : 1;
            line.loop = true;
            line.positionCount = segments + 1 + innerPoints;
            int index = 0;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-half, half, i / (float)segments) * Mathf.Deg2Rad;
                line.SetPosition(index++, ShapePoint(Mathf.Cos(angle) * outerRadius, Mathf.Sin(angle) * outerRadius));
            }
            if (innerRadius > 0.01f)
            {
                for (int i = segments; i >= 0; i--)
                {
                    float angle = Mathf.Lerp(-half, half, i / (float)segments) * Mathf.Deg2Rad;
                    line.SetPosition(index++, ShapePoint(Mathf.Cos(angle) * innerRadius, Mathf.Sin(angle) * innerRadius));
                }
            }
            else line.SetPosition(index, ShapePoint(0f, 0f));
        }

        void SetCross(LineRenderer line, float width, float height, float thickness)
        {
            float hx = width * 0.5f;
            float hy = height * 0.5f;
            float t = Mathf.Clamp(thickness * 0.5f, 0.02f, Mathf.Min(hx, hy));
            Vector2[] points =
            {
                new Vector2(-t, hy), new Vector2(t, hy), new Vector2(t, t),
                new Vector2(hx, t), new Vector2(hx, -t), new Vector2(t, -t),
                new Vector2(t, -hy), new Vector2(-t, -hy), new Vector2(-t, -t),
                new Vector2(-hx, -t), new Vector2(-hx, t), new Vector2(-t, t),
            };
            line.loop = true;
            line.positionCount = points.Length;
            for (int i = 0; i < points.Length; i++)
                line.SetPosition(i, ShapePoint(points[i].x, points[i].y));
        }

        void SetBox(LineRenderer line, float width, float height)
        {
            float hx = width * 0.5f;
            float hy = height * 0.5f;
            line.loop = true;
            line.positionCount = 4;
            line.SetPosition(0, ShapePoint(-hx, -hy));
            line.SetPosition(1, ShapePoint(-hx, hy));
            line.SetPosition(2, ShapePoint(hx, hy));
            line.SetPosition(3, ShapePoint(hx, -hy));
        }

        void SetCone(LineRenderer line, float width, float height)
        {
            float hx = width * 0.5f;
            float hy = height * 0.5f;
            line.loop = true;
            line.positionCount = 3;
            line.SetPosition(0, ShapePoint(-hx, 0f));
            line.SetPosition(1, ShapePoint(hx, hy));
            line.SetPosition(2, ShapePoint(hx, -hy));
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
            if (!PassesSpatialFilter(other)) return;

            // 音声アイテムへのヒット（中立物：陣営問わず誰でも殴れる。最後に削った人が取得者）
            var voiceItem = other.GetComponentInParent<Battle.VoiceItem>();
            if (voiceItem != null && !_hitVoiceItems.Contains(voiceItem))
            {
                if (!SkillCastHitRegistry.TryClaim(SharedCastId, voiceItem, this, SharedHitLockSeconds)) return;
                _hitVoiceItems.Add(voiceItem);
                voiceItem.TakeHit(Damage, Owner);
                _hitsLanded++;
                if (_hitsLanded >= MaxHits && _col != null)
                {
                    _col.enabled = false;
                    if (IsTrap) TriggerTrapBurst();
                }
                return;
            }

            // 召喚物へのヒット
            var summon = other.GetComponentInParent<Battle.SummonEntity>();
            if (summon != null && summon.Owner != Owner && !_hitSummons.Contains(summon))
            {
                if (!SkillCastHitRegistry.TryClaim(SharedCastId, summon, this, SharedHitLockSeconds)) return;
                _hitSummons.Add(summon);
                summon.TakeHit(Damage);
                _hitsLanded++;
                if (_hitsLanded >= MaxHits && _col != null)
                {
                    _col.enabled = false;
                    if (IsTrap) TriggerTrapBurst();
                }
                return;
            }

            // 破壊可能な障害物（壁など）へのヒット（陣営問わず誰でも殴れる中立物）
            var destructible = other.GetComponentInParent<Battle.DestructibleObstacle>();
            if (destructible != null && !_hitDestructibles.Contains(destructible))
            {
                if (!SkillCastHitRegistry.TryClaim(SharedCastId, destructible, this, SharedHitLockSeconds)) return;
                _hitDestructibles.Add(destructible);
                destructible.TakeHit(Damage, Owner);
                _hitsLanded++;
                if (_hitsLanded >= MaxHits && _col != null)
                {
                    _col.enabled = false;
                    if (IsTrap) TriggerTrapBurst();
                }
                return;
            }

            var target = other.GetComponentInParent<Fighter>();
            if (target == null || target == Owner) return;
            // フレンドリーファイアOFF：同陣営には当てない（1v1はfighter1=Players/fighter2=Enemiesで別陣営）。
            // チュートリアルの練習台は陣営に関わらずどちらからも攻撃が通る。
            if (Owner != null && target.Team == Owner.Team && !target.IsPracticeDummy) return;
            if (target.IsDodging) return;
            if (MaxHits <= 1 && _hitTargets.Contains(target)) return;
            if (MaxHits > 1 &&
                _nextHitTimes.TryGetValue(target, out float nextTime) &&
                Time.time < nextTime) return;
            if (!SkillCastHitRegistry.TryClaim(SharedCastId, target, this, SharedHitLockSeconds)) return;

            _hitTargets.Add(target);
            ApplyHit(target);
            _hitsLanded++;
            if (MaxHits > 1)
                _nextHitTimes[target] = Time.time + Mathf.Max(0.04f, Lifetime / Mathf.Max(1, MaxHits));
            if (_hitsLanded >= MaxHits && _col != null)
            {
                // コライダーを無効化してビジュアルは lifetime まで表示し続ける
                _col.enabled = false;
                // 設置技は「使用済みの罠が残って見える」と紛らわしいので、爆発演出を出して消す
                if (IsTrap) TriggerTrapBurst();
            }
        }

        // annulus / arc / cross は大きなBoxColliderをbroad-phaseにし、実際の可視形状と同じ
        // 数学的領域へここで絞り込む。line / column は回転済みBoxColliderそのものが最終形状。
        bool PassesSpatialFilter(Collider2D other)
        {
            if (other == null || string.IsNullOrEmpty(SpatialShape)) return true;
            string shape = SpatialShape;
            if (shape == "box" || shape == "line" || shape == "column" || shape == "capsule")
                return true;
            if (shape != "annulus" && shape != "arc" && shape != "cross" && shape != "cone")
                return true;

            Bounds b = other.bounds;
            if (shape == "annulus")
                return BoundsIntersectsAnnulus(b);

            Vector2 closest = other.ClosestPoint(transform.position);
            Vector2[] samples =
            {
                b.center,
                closest,
                new Vector2(b.min.x, b.min.y),
                new Vector2(b.min.x, b.max.y),
                new Vector2(b.max.x, b.min.y),
                new Vector2(b.max.x, b.max.y),
            };

            for (int i = 0; i < samples.Length; i++)
                if (ContainsSpatialPoint(samples[i], shape))
                    return true;

            // 扇・十字・三角形の細い境界を大きなhurtboxが跨ぐ場合に、角だけの検査で
            // 取りこぼさないようbounds各辺も細分して検査する。
            for (int i = 1; i < 8; i++)
            {
                float t = i / 8f;
                if (ContainsSpatialPoint(new Vector2(Mathf.Lerp(b.min.x, b.max.x, t), b.min.y), shape) ||
                    ContainsSpatialPoint(new Vector2(Mathf.Lerp(b.min.x, b.max.x, t), b.max.y), shape) ||
                    ContainsSpatialPoint(new Vector2(b.min.x, Mathf.Lerp(b.min.y, b.max.y, t)), shape) ||
                    ContainsSpatialPoint(new Vector2(b.max.x, Mathf.Lerp(b.min.y, b.max.y, t)), shape))
                    return true;
            }
            return false;
        }

        bool BoundsIntersectsAnnulus(Bounds bounds)
        {
            Vector2[] polygon =
            {
                ToShapeLocal(new Vector2(bounds.min.x, bounds.min.y)),
                ToShapeLocal(new Vector2(bounds.min.x, bounds.max.y)),
                ToShapeLocal(new Vector2(bounds.max.x, bounds.max.y)),
                ToShapeLocal(new Vector2(bounds.max.x, bounds.min.y)),
            };
            float minDistance = bounds.Contains(transform.position) ? 0f : float.MaxValue;
            float maxDistance = 0f;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Length];
                maxDistance = Mathf.Max(maxDistance, a.magnitude);
                minDistance = Mathf.Min(minDistance, DistanceToSegment(Vector2.zero, a, b));
            }

            float outer = Mathf.Min(Mathf.Max(0.01f, DesiredWorldSize.x),
                                    Mathf.Max(0.01f, DesiredWorldSize.y)) * 0.5f;
            float inner = Mathf.Clamp(SpatialInnerRadius, 0f, Mathf.Max(0f, outer - 0.05f));
            return minDistance <= outer && maxDistance >= inner;
        }

        Vector2 ToShapeLocal(Vector2 worldPoint)
        {
            Vector2 delta = worldPoint - (Vector2)transform.position;
            float inverseAngle = -transform.eulerAngles.z * Mathf.Deg2Rad;
            float cos = Mathf.Cos(inverseAngle);
            float sin = Mathf.Sin(inverseAngle);
            return new Vector2(delta.x * cos - delta.y * sin,
                               delta.x * sin + delta.y * cos);
        }

        static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denominator = ab.sqrMagnitude;
            if (denominator <= 0.000001f) return Vector2.Distance(point, a);
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
            return Vector2.Distance(point, a + ab * t);
        }

        bool ContainsSpatialPoint(Vector2 worldPoint, string shape)
        {
            Vector2 p = ToShapeLocal(worldPoint);

            float width  = Mathf.Max(0.01f, DesiredWorldSize.x);
            float height = Mathf.Max(0.01f, DesiredWorldSize.y);
            if (shape == "cross")
            {
                float thickness = SpatialCrossThickness > 0f
                    ? SpatialCrossThickness
                    : Mathf.Min(width, height) * 0.3f;
                bool horizontal = Mathf.Abs(p.x) <= width * 0.5f && Mathf.Abs(p.y) <= thickness * 0.5f;
                bool vertical   = Mathf.Abs(p.y) <= height * 0.5f && Mathf.Abs(p.x) <= thickness * 0.5f;
                return horizontal || vertical;
            }
            if (shape == "cone")
            {
                float along = p.x + width * 0.5f;
                if (along < 0f || along > width) return false;
                float halfHeight = height * 0.5f * (along / width);
                return Mathf.Abs(p.y) <= halfHeight;
            }

            float outer = Mathf.Min(width, height) * 0.5f;
            float radius = p.magnitude;
            float inner = Mathf.Clamp(SpatialInnerRadius, 0f, Mathf.Max(0f, outer - 0.05f));
            if (radius < inner || radius > outer) return false;
            if (shape == "annulus") return true;

            float halfArc = Mathf.Clamp(SpatialArcAngle > 0f ? SpatialArcAngle : 90f, 1f, 360f) * 0.5f;
            float angle = Mathf.Abs(Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg);
            return angle <= halfArc;
        }

        // 設置技の触発演出: 一瞬白く光って膨らみながら消える。
        void TriggerTrapBurst()
        {
            if (_exhausted || _released) return;
            _exhausted = true;
            StopAllCoroutines(); // 寿命待ちを打ち切って爆発演出へ
            StartCoroutine(TrapBurst());
        }

        IEnumerator TrapBurst()
        {
            const float dur = 0.14f;
            float t = 0f;
            float baseAlpha = _sr != null ? _sr.color.a : 1f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                transform.localScale = _visualBaseScale * (1f + 0.5f * k);
                if (_sr != null && !HideVisual)
                    _sr.color = new Color(1f, 1f, 1f, Mathf.Lerp(baseAlpha, 0f, k));
                yield return null;
            }
            Release();
        }

        void ApplyHit(Fighter target)
        {
            Vector2 targetCenter = target.transform.position + Vector3.up * 0.8f;
            if (SpatialKnockbackMode == "along_attack" || SpatialKnockbackMode == "along")
            {
                Vector2 along = transform.right;
                if (along.sqrMagnitude < 0.001f) along = Vector2.right;
                KnockbackDir = along.normalized;
                FixedKnockbackDir = true;
            }
            else if (SpatialKnockbackMode == "from_origin" || SpatialKnockbackMode == "from" ||
                     SpatialKnockbackMode == "toward_origin")
            {
                Vector2 radial = targetCenter - SpatialKnockbackOrigin;
                if (SpatialKnockbackMode == "toward_origin") radial = -radial;
                if (radial.sqrMagnitude < 0.001f) radial = transform.right;
                KnockbackDir = radial.normalized;
                FixedKnockbackDir = true;
            }

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

            // 属性色つきヒットスパークを「接触点」に出す（体の中心ではなく当たった場所で光らせる）
            Vector3 contact = _col != null
                ? _col.bounds.ClosestPoint(target.transform.position + Vector3.up * 0.9f)
                : target.transform.position + Vector3.up * 0.9f;
            Battle.SimpleFX.HitSpark(contact, SkillEnumParser.ElementColor(Element),
                Mathf.Clamp(0.75f + Damage * 0.045f, 0.75f, 1.6f));

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
