using UnityEngine;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;

namespace PromptFighters.Battle
{
    // 音声アイテム（スマッシュボール式）。
    // 空中をふわふわ漂い、攻撃で耐久(MaxHP)を削られる。耐久を0にした
    // 「最後の攻撃者」が取得者となり、onBroken で通知する。
    // Hitbox / Projectile から TakeHit(dmg, attacker) を呼ばれる（陣営問わず誰でも殴れる中立物）。
    // ※当たり判定はトリガーのみ（物理的に乗れない）。攻撃トリガー検出のため Rigidbody2D を持つ。
    public class VoiceItem : MonoBehaviour
    {
        // 現在ステージに存在するアイテム（CPUの標的探索用）。同時に1個まで。
        public static VoiceItem Active { get; private set; }

        public const float MaxHP = 20f;

        float   _hp = MaxHP;
        Fighter _lastAttacker;
        bool    _broken;
        System.Action<Fighter> _onBroken;

        SpriteRenderer _ballSr;
        Color          _ballBaseColor = Color.white;
        Sprite[]       _stages;        // 耐久に応じた3段階テクスチャ（[0]無傷→[2]破損寸前）
        int            _curStage = -1;
        static Sprite[] _ballStages; static bool _ballStagesTried;
        Transform   _visual;   // 脈動はここに適用（ルートのRigidbody2Dと干渉させない）
        Rigidbody2D _rb;       // 移動は MovePosition で行い、攻撃トリガーを確実に発火させる
        Vector3 _basePos;
        Vector2 _knockVel;     // 被弾ノックバック速度（位置へ積分し減衰）
        float   _seed;
        float   _driftDir = 1f;
        float   _halfRangeX;

        const float KnockDamping = 3.2f;  // ノックバックの減衰（大きいほど早く止まる）
        const float MinY = -1.5f;         // 浮遊できる縦範囲
        const float MaxY =  3.8f;

        static readonly Color GoldGlow = new Color(1f, 0.82f, 0.18f, 0.9f);

        public static VoiceItem Spawn(Vector2 pos, float halfRangeX, System.Action<Fighter> onBroken)
        {
            var go = new GameObject("VoiceItem");
            go.transform.position = pos;

            // ファイターと同じレイヤーに置く（接地判定 groundLayer から除外され「乗れない」）。
            var f = BattleManager.Instance?.fighter1;
            if (f != null) go.layer = f.gameObject.layer;

            // 攻撃（Hitbox/Projectileのトリガー）を検出させるため Rigidbody2D が必要。
            // 物理で動かないよう重力ゼロ＋回転固定。位置は Update で transform 制御する。
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType     = RigidbodyType2D.Dynamic;
            rb.constraints  = RigidbodyConstraints2D.FreezeRotation;
            rb.sleepMode    = RigidbodySleepMode2D.NeverSleep;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var item = go.AddComponent<VoiceItem>();
            item._rb = rb;
            item._onBroken   = onBroken;
            item._basePos    = pos;
            item._seed       = Random.value * 10f;
            item._driftDir   = Random.value < 0.5f ? -1f : 1f;
            item._halfRangeX = Mathf.Max(1f, halfRangeX);

            // 見た目コンテナ（脈動はここに適用。ルートは物理＋位置のみ）
            var visualGo = new GameObject("Visual");
            visualGo.transform.SetParent(go.transform, false);
            item._visual = visualGo.transform;

            var stages = BallStages();
            if (stages[0] != null)
            {
                // 生成画像（金/シアンのエネルギー球＋マイク紋章）。マイクが正立するよう回転はしない。
                var ballGo = new GameObject("Ball");
                ballGo.transform.SetParent(visualGo.transform, false);
                item._ballSr = ballGo.AddComponent<SpriteRenderer>();
                item._ballSr.sortingOrder = 9;
                item._ballBaseColor = Color.white;
                item._stages = stages;
                item.UpdateBallStage();    // 初期段階（無傷）を設定
            }
            else
            {
                // フォールバック：従来のグロー二層
                var outerGo = new GameObject("Glow");
                outerGo.transform.SetParent(visualGo.transform, false);
                item._ballSr = outerGo.AddComponent<SpriteRenderer>();
                item._ballSr.sprite = RuntimeSprite.Glow();
                item._ballSr.color  = GoldGlow;
                item._ballBaseColor = GoldGlow;
                item._ballSr.sortingOrder = 9;
                FitSprite(item._ballSr, 2.6f);

                var innerGo = new GameObject("Core");
                innerGo.transform.SetParent(visualGo.transform, false);
                var inner = innerGo.AddComponent<SpriteRenderer>();
                inner.sprite = RuntimeSprite.Glow();
                inner.color  = new Color(1f, 0.98f, 0.85f, 1f);
                inner.sortingOrder = 10;
                FitSprite(inner, 1.2f);
            }

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.7f, 1.7f);

            // 地面に落ちる影（高く浮くほど小さく薄く）。
            float groundY = BattleManager.Instance != null ? BattleManager.Instance.StageGroundY : -1.8f;
            BlobShadow.Spawn(go.transform, groundY, 1.25f, sortingOrder: -2);

            Active = item;
            return item;
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        // 耐久3段階のテクスチャ。[0]無傷 / [1]ひび / [2]破損寸前。
        // 画像が無い段階は手前の段階で代用する（最低でも ball が無いとフォールバック描画）。
        static Sprite[] BallStages()
        {
            if (!_ballStagesTried)
            {
                _ballStages = new Sprite[3];
                _ballStages[0] = Resources.Load<Sprite>("Effects/ball");
                _ballStages[1] = Resources.Load<Sprite>("Effects/ball_crack1");
                _ballStages[2] = Resources.Load<Sprite>("Effects/ball_crack2");
                _ballStagesTried = true;
            }
            return _ballStages;
        }

        // 現在の耐久からテクスチャ段階を決めて切り替える。
        void UpdateBallStage()
        {
            if (_ballSr == null || _stages == null) return;
            float ratio = Mathf.Clamp01(_hp / MaxHP);
            int stage = ratio > 0.66f ? 0 : (ratio > 0.33f ? 1 : 2);
            if (stage == _curStage) return;
            // 欠けている段階は近い段階で代用
            var s = _stages[stage] ?? _stages[Mathf.Max(0, stage - 1)] ?? _stages[0];
            if (s == null) return;
            _ballSr.sprite = s;
            FitSprite(_ballSr, 2.9f);
            _curStage = stage;
        }

        static void FitSprite(SpriteRenderer sr, float worldDiameter)
        {
            float s = sr.sprite != null ? Mathf.Max(0.01f, sr.sprite.bounds.size.x) : 1f;
            sr.transform.localScale = Vector3.one * (worldDiameter / s);
        }

        // 移動は物理ステップで MovePosition（コライダが正しくスイープし攻撃トリガーが発火する）
        void FixedUpdate()
        {
            if (_rb == null) return;
            float dt = Time.fixedDeltaTime;

            // ゆっくり横ドリフト（ステージ内で反復）
            _basePos.x += _driftDir * 0.7f * dt;
            if (_basePos.x >  _halfRangeX) { _basePos.x =  _halfRangeX; _driftDir = -1f; }
            if (_basePos.x < -_halfRangeX) { _basePos.x = -_halfRangeX; _driftDir =  1f; }

            // 被弾ノックバック：速度を位置へ積分しつつ減衰。端で軽く跳ね返る。
            _basePos += (Vector3)(_knockVel * dt);
            _knockVel *= Mathf.Exp(-KnockDamping * dt);
            if (_basePos.x >  _halfRangeX) { _basePos.x =  _halfRangeX; _knockVel.x = -Mathf.Abs(_knockVel.x) * 0.4f; }
            if (_basePos.x < -_halfRangeX) { _basePos.x = -_halfRangeX; _knockVel.x =  Mathf.Abs(_knockVel.x) * 0.4f; }
            _basePos.y = Mathf.Clamp(_basePos.y, MinY, MaxY);

            // ふわふわ上下
            float bob = Mathf.Sin((Time.time + _seed) * 1.6f) * 0.5f;
            _rb.MovePosition(new Vector2(_basePos.x, _basePos.y + bob));
        }

        void Update()
        {
            float t = Time.time + _seed;

            // 脈動で目立たせる。均一スケール（拡大縮小）は奥行き(Z)移動に見えてしまうため、
            // 横伸び⇔縦伸びのスクワッシュ＆ストレッチ（面積ほぼ一定の2D変形）＋明滅で表現する。
            // 残り耐久が減るほど脈動・明滅を強め、割れる直前感を出す。
            if (_visual != null)
            {
                float dmgRatio = 1f - Mathf.Clamp01(_hp / MaxHP);
                float amp   = 0.06f + 0.06f * dmgRatio;
                float speed = 6f + 6f * dmgRatio;
                float wob   = amp * Mathf.Sin(t * speed);
                _visual.localScale = new Vector3(1f + wob, 1f - wob, 1f);

                if (_ballSr != null)
                {
                    float bright = 1f + (0.15f + 0.25f * dmgRatio) * (0.5f + 0.5f * Mathf.Sin(t * speed));
                    _ballSr.color = new Color(
                        _ballBaseColor.r * bright, _ballBaseColor.g * bright,
                        _ballBaseColor.b * bright, _ballBaseColor.a);
                }
            }
        }

        // 取得者（最後に削ったファイター）の識別タグ。1P / 2P / BOSS。
        public static string AcquirerTag(Fighter f)
        {
            var bm = BattleManager.Instance;
            if (bm != null && f != null && f == bm.boss)     return "BOSS";
            if (bm != null && f != null && f == bm.fighter2)  return "2P";
            return "1P";
        }

        // 取得者カラー。1P=青 / 2P=赤 / ボス=黒。
        public static Color AcquirerColor(Fighter f)
        {
            var bm = BattleManager.Instance;
            if (bm != null && f != null && f == bm.boss)     return Color.black;
            if (bm != null && f != null && f == bm.fighter2)  return UITheme.P2Neon;
            return UITheme.P1Neon;
        }

        public void TakeHit(float dmg, Fighter attacker)
        {
            if (_broken || dmg <= 0f) return;
            if (attacker != null) _lastAttacker = attacker;
            _hp -= dmg;
            UpdateBallStage(); // 耐久に応じてテクスチャを切り替え（ひび→破損寸前）

            // ノックバック：攻撃者から離れる向きへ、ダメージに応じて吹き飛ぶ（横メイン＋少し上）
            if (attacker != null)
            {
                float dirX = transform.position.x - attacker.transform.position.x;
                float sign = !Mathf.Approximately(dirX, 0f) ? Mathf.Sign(dirX)
                           : (attacker.FacingRight ? 1f : -1f);
                float kb = Mathf.Clamp(dmg * 0.5f, 2.5f, 9f);
                _knockVel += new Vector2(sign * kb, kb * 0.35f);
            }

            DamagePopup.SpawnText(transform.position + Vector3.up * 0.7f,
                Mathf.RoundToInt(dmg).ToString(), new Color(1f, 0.8f, 0.2f), 1.0f);
            CameraShake.Shake(0.08f, 0.12f);

            if (_hp <= 0f)
            {
                _broken = true;
                // 取得者を一目で分かるよう、取得者カラーで「{タグ} GET!!」を表示（1P=青/2P=赤/ボス=黒）
                string tag = AcquirerTag(_lastAttacker);
                Color  col = AcquirerColor(_lastAttacker);
                DamagePopup.SpawnText(transform.position + Vector3.up * 1.0f,
                    $"{tag} GET!!", col, 2.2f);
                CameraShake.Shake(0.3f, 0.25f);
                _onBroken?.Invoke(_lastAttacker);
                Destroy(gameObject);
            }
        }
    }
}
