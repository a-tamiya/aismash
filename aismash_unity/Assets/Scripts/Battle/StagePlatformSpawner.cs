using UnityEngine;
using System.Collections.Generic;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // 選択中の StageDefinition に従って台・壁を生成・管理する。BattleManager が Awake で自動追加する。
    public class StagePlatformSpawner : MonoBehaviour
    {
        readonly List<GameObject> _active    = new List<GameObject>();
        readonly List<Collider2D> _colliders = new List<Collider2D>();

        void Start()
        {
            var bm = BattleManager.Instance;
            if (bm != null) bm.OnReturnedToSetup += DespawnAll;
        }

        public void SpawnPlatforms()
        {
            DespawnAll();
            var stage = StageRegistry.Current;

            if (stage.platforms != null)
            {
                foreach (var p in stage.platforms)
                {
                    var go = BuildPlatform(new Vector2(p.x, p.y), p.width, stage.platformSpritePath);
                    if (p.moving)
                    {
                        go.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                        var mp = go.AddComponent<MovingPlatform>();
                        mp.originX     = p.x;
                        mp.originY     = p.y;
                        mp.range       = p.moveRange;
                        mp.period      = p.movePeriod;
                        mp.phaseOffset = p.phaseOffset;
                    }
                    var col = go.GetComponent<Collider2D>();
                    _active.Add(go);
                    if (col != null) _colliders.Add(col);
                }
            }

            if (stage.walls != null)
            {
                foreach (var w in stage.walls)
                {
                    var go = BuildWall(new Vector2(w.x, w.y), w.width, w.height, stage.wallSpritePath);
                    var col = go.GetComponent<Collider2D>();
                    _active.Add(go);
                    if (col != null) _colliders.Add(col);
                }
            }
        }

        public void DespawnAll()
        {
            foreach (var go in _active) if (go) Destroy(go);
            _active.Clear();
            _colliders.Clear();
        }

        public List<Collider2D> GetColliders() => _colliders;

        // ── スプライトキャッシュ ────────────────────────────────────────
        static Sprite _defaultPlatSprite; static bool _platTried;
        static Sprite DefaultPlatformSprite()
        {
            if (!_platTried) { _defaultPlatSprite = Resources.Load<Sprite>("Stage/platform"); _platTried = true; }
            return _defaultPlatSprite;
        }

        static Sprite _defaultWallSprite; static bool _wallTried;
        static Sprite DefaultWallSprite()
        {
            if (!_wallTried) { _defaultWallSprite = Resources.Load<Sprite>("Stage/wall"); _wallTried = true; }
            return _defaultWallSprite;
        }

        // ── 踏み台プラットフォーム ──────────────────────────────────────
        static GameObject BuildPlatform(Vector2 center, float width, string spritePath)
        {
            const float ColliderH = 0.30f;
            const float PlatformOpaqueTopFromPivotPixels = 176f;

            var go = new GameObject("StagePlatform");
            go.transform.position = center;

            // ドロップシャドウ
            var shadowGo = new GameObject("PlatShadow");
            shadowGo.transform.SetParent(go.transform, false);
            shadowGo.transform.localPosition = new Vector3(0f, -0.18f, 0f);
            shadowGo.transform.localScale    = new Vector3(width, 0.34f, 1f);
            var shadowSr = shadowGo.AddComponent<SpriteRenderer>();
            shadowSr.sprite       = RuntimeSprite.Circle();
            shadowSr.color        = new Color(0f, 0f, 0f, 0.28f);
            shadowSr.sortingOrder = -6;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;

            var col = go.AddComponent<BoxCollider2D>();
            col.size           = new Vector2(width, ColliderH);
            col.usedByEffector = true;

            var eff = go.AddComponent<PlatformEffector2D>();
            eff.useOneWay         = true;
            eff.useOneWayGrouping = false;
            eff.surfaceArc        = 170f;
            eff.rotationalOffset  = 0f;

            var sprite = (spritePath != null ? Resources.Load<Sprite>(spritePath) : null)
                         ?? DefaultPlatformSprite();

            var vis = new GameObject("PlatVisual");
            vis.transform.SetParent(go.transform, false);
            var sr = vis.AddComponent<SpriteRenderer>();
            sr.sortingOrder = -5;

            if (sprite != null)
            {
                sr.sprite = sprite;
                sr.color  = Color.white;
                float targetW = width * 1.12f;
                float scale   = targetW / Mathf.Max(0.01f, sprite.bounds.size.x);
                vis.transform.localScale = new Vector3(scale, scale, 1f);
                float opaqueTop   = PlatformOpaqueTopFromPivotPixels / sprite.pixelsPerUnit * scale;
                float colliderTop = ColliderH * 0.5f;
                vis.transform.localPosition = new Vector3(0f, colliderTop - opaqueTop, 0f);
            }
            else
            {
                sr.sprite = RuntimeSprite.Square();
                sr.color  = new Color(0.50f, 0.78f, 1.0f, 0.80f);
                vis.transform.localScale = new Vector3(width, 0.22f, 1f);
            }

            return go;
        }

        // ── 壁型オブスタクル（全方向コライダー）────────────────────────
        static GameObject BuildWall(Vector2 center, float width, float height, string spritePath)
        {
            var go = new GameObject("StageWall");
            go.transform.position = center;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(width, height);

            var sprite = (spritePath != null ? Resources.Load<Sprite>(spritePath) : null)
                         ?? DefaultWallSprite();

            var vis = new GameObject("WallVisual");
            vis.transform.SetParent(go.transform, false);
            var sr = vis.AddComponent<SpriteRenderer>();
            sr.sortingOrder = -5;

            if (sprite != null)
            {
                sr.sprite = sprite;
                sr.color  = Color.white;
                float sx = width  * 1.05f / Mathf.Max(0.01f, sprite.bounds.size.x);
                float sy = height * 1.05f / Mathf.Max(0.01f, sprite.bounds.size.y);
                vis.transform.localScale = new Vector3(sx, sy, 1f);
            }
            else
            {
                sr.sprite = RuntimeSprite.Square();
                sr.color  = new Color(0.55f, 0.45f, 0.35f, 0.90f);
                vis.transform.localScale = new Vector3(width, height, 1f);
            }

            return go;
        }
    }
}
