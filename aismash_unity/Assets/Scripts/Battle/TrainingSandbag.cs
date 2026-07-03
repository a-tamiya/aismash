using UnityEngine;
using PromptFighters.Audio;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;

namespace PromptFighters.Battle
{
    // チュートリアル用のサンドバッグ（練習台）。攻撃を受けると揺れて数字を出すが壊れない。
    // Hitbox / Projectile から TakeHit(dmg, attacker) を呼ばれる中立の的（トリガー判定・すり抜け可）。
    // 画像 Resources/Effects/sandbag.png があれば使用、無ければ簡易フォールバック表示。
    public class TrainingSandbag : MonoBehaviour
    {
        SpriteRenderer _sr;
        float _angle;      // 現在の傾き（度）
        float _angVel;     // 角速度
        Color _baseColor = Color.white;
        float _flash;      // 被弾フラッシュ残り

        const float Spring   = 70f;   // 直立へ戻すばね
        const float Damping  = 6.5f;  // 減衰
        const float MaxAngle = 40f;

        static Sprite _cachedSprite;
        static bool   _spriteTried;

        public static TrainingSandbag Spawn(Vector2 pos, Fighter layerOwner)
        {
            var go = new GameObject("TrainingSandbag");
            go.transform.position = pos;
            if (layerOwner != null) go.layer = layerOwner.gameObject.layer;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 6;

            if (!_spriteTried) { _cachedSprite = Resources.Load<Sprite>("Effects/sandbag"); _spriteTried = true; }
            Sprite sprite = _cachedSprite;

            if (sprite != null)
            {
                sr.sprite = sprite;
                float h = Mathf.Max(0.01f, sprite.bounds.size.y);
                go.transform.localScale = Vector3.one * (2.5f / h); // 高さ約2.5に揃える
            }
            else
            {
                // フォールバック：茶色い樽型（画像未追加時でも練習できる）
                sr.sprite = RuntimeSprite.Square();
                sr.color  = new Color(0.5f, 0.36f, 0.22f);
                go.transform.localScale = new Vector3(1.15f, 2.4f, 1f);
            }
            sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, sr.color.a);

            var s = go.AddComponent<TrainingSandbag>();
            s._sr = sr;
            s._baseColor = sr.color;

            // 当たり判定（トリガー・すり抜け）。スプライト範囲に合わせる。
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size   = sr.sprite.bounds.size;
            col.offset = sr.sprite.bounds.center;

            float groundY = BattleManager.Instance != null ? BattleManager.Instance.StageGroundY : pos.y;
            BlobShadow.Spawn(go.transform, groundY, 1.1f, sortingOrder: -2);

            return s;
        }

        public void TakeHit(float dmg, Fighter attacker)
        {
            if (dmg <= 0f) return;
            float dir = attacker != null ? Mathf.Sign(transform.position.x - attacker.transform.position.x) : 1f;
            if (Mathf.Approximately(dir, 0f)) dir = 1f;
            // 攻撃者から離れる向きへ傾ける。ダメージが大きいほど大きく揺れる。
            _angVel += dir * Mathf.Clamp(10f + dmg * 2f, 12f, 48f);
            _flash = 0.12f;

            DamagePopup.SpawnText(transform.position + Vector3.up * 1.7f,
                Mathf.RoundToInt(dmg).ToString(), new Color(1f, 0.85f, 0.35f), 0.9f);
            CameraShake.Shake(0.04f, 0.07f);
        }

        // つかまれた反応（チュートリアルのつかみ練習用）。大きく揺さぶられる。
        public void OnGrabReaction(Fighter by)
        {
            float dir = by != null ? Mathf.Sign(transform.position.x - by.transform.position.x) : 1f;
            if (Mathf.Approximately(dir, 0f)) dir = 1f;
            _angVel += dir * 55f;
            _flash = 0.16f;
            DamagePopup.SpawnText(transform.position + Vector3.up * 1.8f,
                "つかみ！", new Color(0.55f, 0.9f, 1f), 1.2f);
            GameAudioManager.Instance?.PlayGrab();
            CameraShake.Shake(0.08f, 0.12f);
        }

        void Update()
        {
            // ばね＋減衰で直立へ戻す（ボップバッグの揺れ）
            _angVel += (-Spring * _angle - Damping * _angVel) * Time.deltaTime;
            _angle = Mathf.Clamp(_angle + _angVel * Time.deltaTime, -MaxAngle, MaxAngle);
            transform.rotation = Quaternion.Euler(0f, 0f, -_angle);

            if (_flash > 0f && _sr != null)
            {
                _flash -= Time.deltaTime;
                float k = Mathf.Clamp01(_flash / 0.12f);
                _sr.color = Color.Lerp(_baseColor, Color.white, 0.6f * k);
            }
        }
    }
}
