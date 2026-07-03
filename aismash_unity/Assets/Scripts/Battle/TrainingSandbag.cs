using UnityEngine;
using PromptFighters.Audio;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;
using PromptFighters.Utils;

namespace PromptFighters.Battle
{
    // チュートリアル用のサンドバッグ（練習台）。HP無限・動かない中立の的で、殴られても
    // その場に立ったまま（フラッシュ＋ダメージ数値のみ）。壊れない。「掴んで投げる」練習にのみ対応し、
    // 投げられた後は元の位置へ戻さず、着地した場所にそのまま立ち続ける。
    // Hitbox / Projectile から TakeHit(dmg, attacker) を呼ばれる中立の的（トリガー判定・すり抜け可）。
    // 画像 Resources/Effects/sandbag.png があれば使用、無ければ簡易フォールバック表示。
    public class TrainingSandbag : MonoBehaviour
    {
        SpriteRenderer _sr;
        Color _baseColor = Color.white;
        float _flash;      // 被弾フラッシュ残り
        float _groundY;    // 設置された地面の高さ。投げられた後もここへ着地させる。

        // 投げられた/離された後、着地地点まで山なりに飛ぶ演出（着地したらそこに立ち続け、戻らない）。
        Vector3 _flightFrom, _flightTo;
        float   _flightTimer;
        float   _flightArc;
        const float FlightDuration = 0.35f;
        const float ThrowDistance  = 3.2f;   // 投げたときに飛ぶ距離
        const float ThrowArcHeight = 1.1f;   // 投げたときの山なりの高さ

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
            float footPivotY = EstimateFootPivotY(processed);
            return Sprite.Create(processed, new Rect(0, 0, processed.width, processed.height),
                new Vector2(0.5f, footPivotY), processed.height * 0.5f);
        }

        // 透過処理後の画像で「実際に見えている最下段（足元）」のY位置を0-1の比率で返す。
        // 手動で用意した画像はキャラ用の生成プロンプトと違い、下部に透明な余白が残ることがあり、
        // pivot=0固定だと足元が画像下端より高い位置にあるため地面から浮いて見えてしまう。
        static float EstimateFootPivotY(Texture2D processed)
        {
            const int AlphaThreshold = 20;
            int w = processed.width, h = processed.height;
            var px = processed.GetPixels32();
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (px[row + x].a >= AlphaThreshold) return (float)y / h;
                }
            }
            return 0f;
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
            // HP無限・動かない的：殴られてもその場から動かず、フラッシュとダメージ数値だけで反応する。
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
            _flightTimer = 0f;
            transform.rotation = Quaternion.identity;
            GameAudioManager.Instance?.PlayGrab();
            DamagePopup.SpawnText(transform.position + Vector3.up * 1.8f,
                "つかんだ！", new Color(0.55f, 0.9f, 1f), 1.0f);
        }

        // 投げ演出。投げた方向へ山なりに飛び、着地した場所にそのまま立ち続ける（元の位置には戻らない）。
        public void Throw(Vector2 dir)
        {
            if (!IsHeld) return;
            IsHeld = false;
            _heldBy = null;
            float sign = !Mathf.Approximately(dir.x, 0f) ? Mathf.Sign(dir.x) : 1f;
            BeginFlight(new Vector3(transform.position.x + sign * ThrowDistance, _groundY, 0f), ThrowArcHeight);
            _flash = 0.2f;
            DamagePopup.SpawnText(transform.position + Vector3.up * 1.8f,
                "投げた！", new Color(1f, 0.75f, 0.2f), 1.4f);
            GameAudioManager.Instance?.PlayGimmickBuff();
            CameraShake.Shake(0.1f, 0.14f);
        }

        // 掴みを強制解除する（チュートリアル終了時などの後始末用）。その場から真下の地面へ落とす。
        public void ReleaseHeld()
        {
            if (!IsHeld) return;
            IsHeld = false;
            _heldBy = null;
            BeginFlight(new Vector3(transform.position.x, _groundY, 0f), 0f);
        }

        void BeginFlight(Vector3 to, float arcHeight)
        {
            _flightFrom = transform.position;
            _flightTo   = to;
            _flightArc  = arcHeight;
            _flightTimer = FlightDuration;
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

            if (_flightTimer > 0f)
            {
                _flightTimer -= Time.deltaTime;
                float t = 1f - Mathf.Clamp01(_flightTimer / FlightDuration);
                Vector3 pos = Vector3.Lerp(_flightFrom, _flightTo, t);
                pos.y += Mathf.Sin(t * Mathf.PI) * _flightArc;
                transform.position = pos;
                if (_flightTimer <= 0f) transform.position = _flightTo; // 誤差を残さず着地地点へ確定
            }

            if (_flash > 0f && _sr != null)
            {
                _flash -= Time.deltaTime;
                float k = Mathf.Clamp01(_flash / 0.12f);
                _sr.color = Color.Lerp(_baseColor, Color.white, 0.6f * k);
            }
        }
    }
}
