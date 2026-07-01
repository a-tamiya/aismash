using UnityEngine;
using System.Collections.Generic;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // 中段プラットフォームを生成・管理する。BattleManager が Awake で自動追加する。
    public class StagePlatformSpawner : MonoBehaviour
    {
        // (x, y, 幅)
        static readonly (float x, float y, float w)[] Defs =
        {
            (-3.2f, 0.1f, 3.0f),
            ( 3.2f, 0.1f, 3.0f),
        };

        readonly List<GameObject>  _active    = new List<GameObject>();
        readonly List<Collider2D>  _colliders = new List<Collider2D>();

        void Start()
        {
            var bm = BattleManager.Instance;
            if (bm != null) bm.OnReturnedToSetup += DespawnAll;
        }

        public void SpawnPlatforms()
        {
            DespawnAll();
            foreach (var (x, y, w) in Defs)
            {
                var go  = Build(new Vector2(x, y), w);
                var col = go.GetComponent<Collider2D>();
                _active.Add(go);
                if (col != null) _colliders.Add(col);
            }
        }

        public void DespawnAll()
        {
            foreach (var go in _active) if (go) Destroy(go);
            _active.Clear();
            _colliders.Clear();
        }

        public List<Collider2D> GetColliders() => _colliders;

        static Sprite _platformSprite; static bool _platTried;
        static Sprite PlatformSprite()
        {
            if (!_platTried) { _platformSprite = Resources.Load<Sprite>("Stage/platform"); _platTried = true; }
            return _platformSprite;
        }

        static GameObject Build(Vector2 center, float width)
        {
            const float ColliderH = 0.30f;
            const float PlatformOpaqueTopFromPivotPixels = 176f;

            var go = new GameObject("StagePlatform");
            go.transform.position = center;

            // 台の立体感を出すドロップシャドウ（台の真下に柔らかい暗い楕円）。
            var shadowGo = new GameObject("PlatShadow");
            shadowGo.transform.SetParent(go.transform, false);
            shadowGo.transform.localPosition = new Vector3(0f, -0.18f, 0f);
            shadowGo.transform.localScale    = new Vector3(width, 0.34f, 1f);
            var shadowSr = shadowGo.AddComponent<SpriteRenderer>();
            shadowSr.sprite       = RuntimeSprite.Circle();
            shadowSr.color        = new Color(0f, 0f, 0f, 0.28f);
            shadowSr.sortingOrder = -6; // 台ビジュアル(-5)より奥、背景(-10)より手前

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;

            var col = go.AddComponent<BoxCollider2D>();
            col.size          = new Vector2(width, ColliderH);
            col.usedByEffector = true;

            var eff = go.AddComponent<PlatformEffector2D>();
            eff.useOneWay         = true;
            eff.useOneWayGrouping = false;
            eff.surfaceArc        = 170f;
            eff.rotationalOffset  = 0f;

            var sprite = PlatformSprite();
            var vis = new GameObject("PlatVisual");
            vis.transform.SetParent(go.transform, false);
            var sr = vis.AddComponent<SpriteRenderer>();
            sr.sortingOrder = -5; // ファイターより奥（背景-10より手前）に置き、キャラが手前に立って見える

            if (sprite != null)
            {
                sr.sprite = sprite;
                sr.color  = Color.white;
                float targetW = width * 1.12f;                       // 端を少しかぶせる
                float scale   = targetW / Mathf.Max(0.01f, sprite.bounds.size.x);
                vis.transform.localScale = new Vector3(scale, scale, 1f);
                // 透明余白ではなく、台画像の不透明上端をコライダ上面に合わせる。
                float opaqueTop = PlatformOpaqueTopFromPivotPixels / sprite.pixelsPerUnit * scale;
                float colliderTop = ColliderH * 0.5f;
                vis.transform.localPosition = new Vector3(0f, colliderTop - opaqueTop, 0f);
            }
            else
            {
                // フォールバック：従来の青い半透明バー
                sr.sprite = RuntimeSprite.Square();
                sr.color  = new Color(0.50f, 0.78f, 1.0f, 0.80f);
                vis.transform.localScale = new Vector3(width, 0.22f, 1f);
            }

            return go;
        }
    }
}
