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

        // ── スプライト読み込み ──────────────────────────────────────────
        // Sprite 読み込み → グリーンバック除去（G チャンネルが R・B を大きく上回る画素を透過）。
        // isReadable が false の場合は除去をスキップし、そのまま返す。
        static Sprite LoadSprite(string path)
        {
            if (path == null) return null;
            var sprite = Resources.Load<Sprite>(path);
            Texture2D src = sprite != null ? sprite.texture
                                           : Resources.Load<Texture2D>(path);
            if (src == null) return null;
            return Chromakey(src, 100f);
        }

        static Sprite Chromakey(Texture2D src, float ppu)
        {
            if (!src.isReadable)
                return Sprite.Create(src, new Rect(0, 0, src.width, src.height),
                                     new Vector2(0.5f, 0.5f), ppu);

            var pixels = src.GetPixels32();
            bool modified = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                // G が R・B の両方を 1.4 倍以上上回りかつ一定値以上 → グリーンバック
                if (p.g > 80 && p.g > p.r * 1.4f && p.g > p.b * 1.4f)
                {
                    pixels[i] = new Color32(p.r, p.g, p.b, 0);
                    modified = true;
                }
            }
            if (!modified)
                return Sprite.Create(src, new Rect(0, 0, src.width, src.height),
                                     new Vector2(0.5f, 0.5f), ppu);

            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                 new Vector2(0.5f, 0.5f), ppu);
        }

        static Sprite _defaultPlatSprite; static bool _platTried;
        static Sprite DefaultPlatformSprite()
        {
            if (!_platTried) { _defaultPlatSprite = LoadSprite("Stage/platform"); _platTried = true; }
            return _defaultPlatSprite;
        }

        static Sprite _defaultWallSprite; static bool _wallTried;
        static Sprite DefaultWallSprite()
        {
            if (!_wallTried) { _defaultWallSprite = LoadSprite("Stage/wall"); _wallTried = true; }
            return _defaultWallSprite;
        }

        // 壁に張り付き防止のためのフリクションゼロマテリアル
        static PhysicsMaterial2D _noFriction;
        static PhysicsMaterial2D NoFriction()
        {
            if (_noFriction == null)
            {
                _noFriction = new PhysicsMaterial2D("WallNoFriction");
                _noFriction.friction = 0f;
                _noFriction.bounciness = 0f;
            }
            return _noFriction;
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

            var sprite = LoadSprite(spritePath) ?? DefaultPlatformSprite();

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
            col.sharedMaterial = NoFriction(); // 壁への張り付き防止

            var sprite = LoadSprite(spritePath) ?? DefaultWallSprite();

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
