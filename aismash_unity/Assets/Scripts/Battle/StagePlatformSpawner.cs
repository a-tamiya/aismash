using UnityEngine;
using System.Collections.Generic;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // 中段プラットフォームを生成・管理する。BattleManager が Awake で自動追加する。
    public class StagePlatformSpawner : MonoBehaviour
    {
        public static bool PlatformsEnabled = true;

        // (x, y, 幅)
        static readonly (float x, float y, float w)[] Defs =
        {
            (-3.2f, 0.38f, 3.0f),
            ( 3.2f, 0.38f, 3.0f),
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
            if (!PlatformsEnabled) return;
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

        static GameObject Build(Vector2 center, float width)
        {
            const float H = 0.22f;

            var go = new GameObject("StagePlatform");
            go.transform.position = center;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;

            var col = go.AddComponent<BoxCollider2D>();
            col.size          = Vector2.one;
            col.usedByEffector = true;

            var eff = go.AddComponent<PlatformEffector2D>();
            eff.useOneWay         = true;
            eff.useOneWayGrouping = false;
            eff.surfaceArc        = 170f;
            eff.rotationalOffset  = 0f;

            // 本体ビジュアル
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = RuntimeSprite.Square();
            sr.color        = new Color(0.50f, 0.78f, 1.0f, 0.80f);
            sr.sortingOrder = 6;
            go.transform.localScale = new Vector3(width, H, 1f);

            // 上端の明るいライン
            var edge = new GameObject("PlatEdge");
            edge.transform.SetParent(go.transform, false);
            edge.transform.localPosition = new Vector3(0f, 0.5f - 0.5f * (0.04f / H), 0f);
            edge.transform.localScale    = new Vector3(1f, 0.04f / H, 1f);
            var edgeSr = edge.AddComponent<SpriteRenderer>();
            edgeSr.sprite       = RuntimeSprite.Square();
            edgeSr.color        = new Color(0.90f, 0.97f, 1.0f, 1.0f);
            edgeSr.sortingOrder = 7;

            return go;
        }
    }
}
