using UnityEngine;

namespace PromptFighters.Battle
{
    // バトルカメラ。生存ファイター全員（1v1は2人、協力はボス含む）が
    // 画面に収まるよう、距離に応じて自動ズーム・追従する。
    // カメラ揺れ（CameraShake）の適用もここで行い、カメラ制御を一本化する。
    // KOスロー中は BattleManager の KO ズーム演出を優先して追従を止める。
    // 表示範囲は背景スプライト内に収め、外側の黒い空間が映らないようにする。
    [RequireComponent(typeof(Camera))]
    public class BattleCamera : MonoBehaviour
    {
        [Range(0.4f, 1f)] public float minZoomRatio = 0.82f; // 最大ズームイン時のサイズ比（小さいほど寄る・酔いやすい）
        public float paddingX    = 3.6f;  // ファイターの外側に確保する横余白（ワールド）
        public float paddingY    = 2.3f;  // 同・縦余白
        public float followSpeed = 3.2f;  // 位置の追従速度（大きいほど機敏）
        public float zoomSpeed    = 2.4f; // ズームの追従速度（位置より緩やかにして酔いを抑える）

        Camera  _cam;
        float   _defaultSize;
        Vector3 _defaultPos;
        Vector3 _prevShake;

        // 背景スプライトのワールド境界（この範囲外＝黒い空間は映さない）。
        bool  _hasBounds;
        float _bgMinX, _bgMaxX, _bgMinY, _bgMaxY;

        void Awake()
        {
            _cam         = GetComponent<Camera>();
            _defaultSize = _cam.orthographicSize;
            _defaultPos  = transform.position;
            CacheStageBounds();
        }

        // 背景スプライト（最も奥＝sortingOrder最小の大きなSpriteRenderer）の境界を取得。
        void CacheStageBounds()
        {
            SpriteRenderer best = null;
            var srs = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
            foreach (var sr in srs)
            {
                if (sr.sprite == null) continue;
                var s = sr.bounds.size;
                if (s.x < 10f || s.y < 6f) continue; // 背景級の大きさのみ対象
                if (best == null || sr.sortingOrder < best.sortingOrder)
                    best = sr;
            }
            if (best == null) { _hasBounds = false; return; }
            var b = best.bounds;
            _bgMinX = b.min.x; _bgMaxX = b.max.x;
            _bgMinY = b.min.y; _bgMaxY = b.max.y;
            _hasBounds = true;
        }

        void LateUpdate()
        {
            // 前フレームの揺れオフセットを戻してから処理する（基準位置を汚さない）
            transform.position -= _prevShake;
            _prevShake = Vector3.zero;

            var bm = BattleManager.Instance;
            if (bm != null && ShouldTrack(bm))
                TrackFighters(bm);

            _prevShake = CameraShake.EvaluateOffset(Time.unscaledDeltaTime);
            transform.position += _prevShake;
        }

        static bool ShouldTrack(BattleManager bm)
        {
            if (bm.IsKoSlowActive) return false; // KOズーム演出を優先
            return bm.Phase == BattlePhase.Fighting ||
                   bm.Phase == BattlePhase.Training ||
                   bm.Phase == BattlePhase.Countdown;
        }

        void TrackFighters(BattleManager bm)
        {
            // 生存ファイターの範囲（体の中心あたり = 足元+1.0）を集計
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            int count = 0;
            CollectFighter(bm.fighter1, ref minX, ref maxX, ref minY, ref maxY, ref count);
            CollectFighter(bm.fighter2, ref minX, ref maxX, ref minY, ref maxY, ref count);
            if (bm.Mode == BattleMode.CoopVsBoss)
                CollectFighter(bm.boss, ref minX, ref maxX, ref minY, ref maxY, ref count);
            if (count < 2) return; // 1人以下なら現状維持（KO処理などに任せる）

            float aspect = Mathf.Max(0.1f, _cam.aspect);

            // 全員＋余白が収まるサイズ。デフォルトより引かず、寄りすぎもしない。
            float needHalfW = (maxX - minX) * 0.5f + paddingX;
            float needHalfH = (maxY - minY) * 0.5f + paddingY;
            float size = Mathf.Max(needHalfW / aspect, needHalfH, _defaultSize * minZoomRatio);
            size = Mathf.Min(size, _defaultSize);

            float halfW = size * aspect;
            float halfH = size;

            // 注視点（全員の中心）。縦は少し上を見て空中戦を捉えやすくする。
            float cx = (minX + maxX) * 0.5f;
            float cy = (minY + maxY) * 0.5f + halfH * 0.12f;

            // 表示範囲を背景内にクランプ（黒い空間を映さない）。
            if (_hasBounds)
            {
                cx = ClampCenter(cx, halfW, _bgMinX, _bgMaxX, _defaultPos.x);
                cy = ClampCenter(cy, halfH, _bgMinY, _bgMaxY, _defaultPos.y);
            }
            else
            {
                float maxCamX = Mathf.Max(0f, bm.stageHalfWidth + 1.5f - halfW);
                cx = Mathf.Clamp(cx, -maxCamX, maxCamX);
                cy = Mathf.Max(_defaultPos.y, cy);
            }

            // ズームは位置より緩やかに、位置は機敏に追従（酔い軽減）。
            float kPos  = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            float kZoom = 1f - Mathf.Exp(-zoomSpeed   * Time.deltaTime);
            _cam.orthographicSize = Mathf.Lerp(_cam.orthographicSize, size, kZoom);
            transform.position = Vector3.Lerp(
                transform.position, new Vector3(cx, cy, _defaultPos.z), kPos);
        }

        // 表示半幅 half の枠が [min,max] 内に収まる中心値を返す。
        // 枠が範囲より大きい場合は範囲中心（fallbackは未使用）に寄せる。
        static float ClampCenter(float center, float half, float min, float max, float fallback)
        {
            float lo = min + half;
            float hi = max - half;
            if (lo > hi) return (min + max) * 0.5f; // 枠が範囲より大きい→中央固定
            return Mathf.Clamp(center, lo, hi);
        }

        static void CollectFighter(Fighter f,
            ref float minX, ref float maxX, ref float minY, ref float maxY, ref int count)
        {
            if (f == null || !f.gameObject.activeInHierarchy) return;
            if (f.State == FighterState.Dead) return;
            Vector3 p = f.transform.position + Vector3.up * 1.0f;
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
            count++;
        }
    }
}
