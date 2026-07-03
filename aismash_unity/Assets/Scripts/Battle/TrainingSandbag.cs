using UnityEngine;
using PromptFighters.Audio;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;
using PromptFighters.Utils;

namespace PromptFighters.Battle
{
    // チュートリアル用のサンドバッグ（練習台）。攻撃を受けるとノックバックして跳ね返り、
    // 傾きながら元の位置へ戻る。壊れない。「掴んで投げる」練習にも対応する。
    // Hitbox / Projectile から TakeHit(dmg, attacker) を呼ばれる中立の的（トリガー判定・すり抜け可）。
    // 画像 Resources/Effects/sandbag.png があれば使用、無ければ簡易フォールバック表示。
    public class TrainingSandbag : MonoBehaviour
    {
        SpriteRenderer _sr;
        float _angle;      // 現在の傾き（度）
        float _angVel;     // 角速度
        Color _baseColor = Color.white;
        float _flash;      // 被弾フラッシュ残り

        Vector3 _basePos;   // 揺れ・ノックバックが戻ってくる基準位置（Y座標は常に地面の高さ）
        float   _groundY;   // 設置された地面の高さ。掴まれて浮いても、離したらここへ戻す。
        float   _offsetX;   // 基準位置からの横方向オフセット（ノックバック変位）
        float   _offsetVelX;
        float   _offsetY;   // 掴まれて浮いた高さ分のオフセット（投げたら滑らかに0=地面へ戻す）

        const float Spring     = 70f;   // 傾きを直立へ戻すばね
        const float Damping    = 6.5f;  // 傾きの減衰
        const float MaxAngle   = 40f;
        const float PosSpring  = 35f;   // 横位置を戻すばね
        const float PosDamping = 4.5f;  // 横位置の減衰
        const float MaxOffset  = 2.4f;  // 横方向へ吹き飛ぶ最大距離
        const float FallLerpSpeed = 6f; // 投げられた後、地面の高さへ戻る速さ

        // つかまれ状態（チュートリアルの「掴んで投げる」練習用）
        public bool IsHeld { get; private set; }
        Fighter _heldBy;
        float _holdTimer;
        const float MaxHoldSeconds = 4f; // 掴んだまま放置された場合の自動解除

        static Sprite _cachedSprite;
        static bool   _spriteTried;

        // Enter Play Mode Options でドメインリロードを無効化しているため、静的キャッシュは
        // エディタの再生を止めても残り続ける。新しいプレイセッション開始のたびに必ずリセットする。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticCacheOnPlay()
        {
            _cachedSprite = null;
            _spriteTried = false;
        }

        // グリーンバック(#00FF00)のキャラ風画像を足元ピボット(0.5, 0)で読み込み、透過して1回だけキャッシュする。
        static Sprite LoadChromaKeySprite(string resourcePath)
        {
            var tex = Resources.Load<Texture2D>(resourcePath);
            if (tex == null) return null;
            if (!tex.isReadable)
            {
                Debug.LogWarning($"[TrainingSandbag] {resourcePath} は isReadable=false のため透過処理できません（Import設定を確認してください）");
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f), tex.height * 0.5f);
            }
            var processed = WhiteBackgroundRemover.ApplyChromaGreen(tex);
            return Sprite.Create(processed, new Rect(0, 0, processed.width, processed.height),
                new Vector2(0.5f, 0f), processed.height * 0.5f);
        }

        public static TrainingSandbag Spawn(Vector2 pos, Fighter layerOwner)
        {
            var go = new GameObject("TrainingSandbag");
            go.transform.position = pos;
            if (layerOwner != null) go.layer = layerOwner.gameObject.layer;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 6;

            if (!_spriteTried) { _cachedSprite = LoadChromaKeySprite("Effects/sandbag"); _spriteTried = true; }
            Sprite sprite = _cachedSprite;

            if (sprite != null)
            {
                sr.sprite = sprite;
                float h = Mathf.Max(0.01f, sprite.bounds.size.y);
                go.transform.localScale = Vector3.one * (2.2f / h); // 高さ約2.2に揃える（キャラよりひと回り小さめ）
            }
            else
            {
                // フォールバック：茶色い樽型（画像未追加時でも練習できる）
                sr.sprite = RuntimeSprite.Square();
                sr.color  = new Color(0.5f, 0.36f, 0.22f);
                go.transform.localScale = new Vector3(1.15f, 2.4f, 1f);
            }
            sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, sr.color.a);

            float groundY = BattleManager.Instance != null ? BattleManager.Instance.StageGroundY : pos.y;

            var s = go.AddComponent<TrainingSandbag>();
            s._sr = sr;
            s._baseColor = sr.color;
            s._groundY = groundY;
            s._basePos = new Vector3(pos.x, groundY, 0f);

            // 当たり判定（トリガー・すり抜け）。スプライト範囲に合わせる。
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size   = sr.sprite.bounds.size;
            col.offset = sr.sprite.bounds.center;

            BlobShadow.Spawn(go.transform, groundY, 1.1f, sortingOrder: -2);

            return s;
        }

        public void TakeHit(float dmg, Fighter attacker)
        {
            if (dmg <= 0f || IsHeld) return; // 掴まれている間は殴られても動かない
            float dir = attacker != null ? Mathf.Sign(transform.position.x - attacker.transform.position.x) : 1f;
            if (Mathf.Approximately(dir, 0f)) dir = 1f;
            // 攻撃者から離れる向きへノックバック＋傾く。ダメージが大きいほど大きく揺れる。
            float power = Mathf.Clamp(10f + dmg * 2f, 12f, 48f);
            _angVel     += dir * power;
            _offsetVelX += dir * Mathf.Clamp(dmg * 0.4f, 1.5f, 6f);
            _flash = 0.12f;

            DamagePopup.SpawnText(transform.position + Vector3.up * 1.7f,
                Mathf.RoundToInt(dmg).ToString(), new Color(1f, 0.85f, 0.35f), 0.9f);
            CameraShake.Shake(0.04f, 0.07f);
        }

        // つかみ開始（チュートリアル用）。以降はプレイヤーの前方に追従する。
        public void BeginHeld(Fighter by)
        {
            if (IsHeld || by == null) return;
            IsHeld = true;
            _heldBy = by;
            _holdTimer = MaxHoldSeconds;
            _angVel = 0f; _angle = 0f;
            _offsetVelX = 0f; _offsetX = 0f; _offsetY = 0f;
            GameAudioManager.Instance?.PlayGrab();
            DamagePopup.SpawnText(transform.position + Vector3.up * 1.8f,
                "つかんだ！", new Color(0.55f, 0.9f, 1f), 1.0f);
        }

        // 投げ演出。既存のノックバックのばね機構を使って大きく吹き飛ばす。
        public void Throw(Vector2 dir)
        {
            if (!IsHeld) return;
            SettleAfterRelease();
            float sign = !Mathf.Approximately(dir.x, 0f) ? Mathf.Sign(dir.x) : 1f;
            _offsetVelX += sign * 15f;
            _angVel     += sign * 90f;
            _flash = 0.2f;
            DamagePopup.SpawnText(transform.position + Vector3.up * 1.8f,
                "投げた！", new Color(1f, 0.75f, 0.2f), 1.4f);
            GameAudioManager.Instance?.PlayGimmickBuff();
            CameraShake.Shake(0.1f, 0.14f);
        }

        // 掴みを強制解除する（チュートリアル終了時などの後始末用）。
        public void ReleaseHeld()
        {
            if (!IsHeld) return;
            SettleAfterRelease();
        }

        // つかまれて浮いていた分を _offsetY に積み、基準位置(_basePos)を地面の高さへ戻す。
        // Update側のばねで滑らかに0へ減衰し、地面に着地して見える。
        void SettleAfterRelease()
        {
            IsHeld = false;
            _heldBy = null;
            _offsetY = transform.position.y - _groundY;
            _basePos = new Vector3(transform.position.x, _groundY, 0f);
        }

        void Update()
        {
            if (IsHeld)
            {
                _holdTimer -= Time.deltaTime;
                if (_heldBy != null)
                {
                    float dirSign = _heldBy.FacingRight ? 1f : -1f;
                    Vector3 target = _heldBy.transform.position + new Vector3(dirSign * 1.1f, 0.9f, 0f);
                    transform.position = Vector3.Lerp(transform.position, target, 14f * Time.deltaTime);
                }
                if (_holdTimer <= 0f) ReleaseHeld(); // 放置されたら自動解除
                return;
            }

            // ばね＋減衰で直立・基準位置へ戻す（ボップバッグの揺れ＋ノックバック）
            _angVel += (-Spring * _angle - Damping * _angVel) * Time.deltaTime;
            _angle = Mathf.Clamp(_angle + _angVel * Time.deltaTime, -MaxAngle, MaxAngle);
            transform.rotation = Quaternion.Euler(0f, 0f, -_angle);

            _offsetVelX += (-PosSpring * _offsetX - PosDamping * _offsetVelX) * Time.deltaTime;
            _offsetX = Mathf.Clamp(_offsetX + _offsetVelX * Time.deltaTime, -MaxOffset, MaxOffset);

            // 掴まれて浮いていた高さは、投げた/離した瞬間からゆっくり地面へ着地させる。
            _offsetY = Mathf.Lerp(_offsetY, 0f, FallLerpSpeed * Time.deltaTime);
            if (Mathf.Abs(_offsetY) < 0.01f) _offsetY = 0f;

            transform.position = _basePos + new Vector3(_offsetX, _offsetY, 0f);

            if (_flash > 0f && _sr != null)
            {
                _flash -= Time.deltaTime;
                float k = Mathf.Clamp01(_flash / 0.12f);
                _sr.color = Color.Lerp(_baseColor, Color.white, 0.6f * k);
            }
        }
    }
}
