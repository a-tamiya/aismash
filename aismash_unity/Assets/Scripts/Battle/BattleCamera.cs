using UnityEngine;

namespace PromptFighters.Battle
{
    // バトルカメラ。生存ファイター全員が画面に収まるよう自動調整するが、
    // 画面酔いを避けるため動きは最小限に抑える：
    //  ・ズーム幅をごく狭く（既定の0.92〜1.0倍）し、デッドゾーン＋超低速で追従。
    //  ・位置もデッドゾーン付きの低速追従。小さな動きではカメラを一切動かさない。
    //  ・「もっと引く」方向（見切れ防止）だけは即座に目標へ反映し、寄る方向は鈍く。
    //  ・表示範囲は背景スプライト内にクランプし、画面外の黒い空間を映さない。
    // KOスロー中は BattleManager の KO ズーム演出を優先して追従を止める。
    [RequireComponent(typeof(Camera))]
    public class BattleCamera : MonoBehaviour
    {
        [Range(0.4f, 1f)] public float minZoomRatio = 0.92f; // 最大ズームイン時のサイズ比（1に近いほど寄らない）
        public float paddingX    = 4.0f;   // ファイターの外側に確保する横余白（ワールド）
        public float paddingY    = 2.6f;   // 同・縦余白
        public float followSpeed = 2.2f;   // 位置の追従速度（低速＝緩やか）
        public float zoomSpeed   = 1.4f;   // ズームの追従速度（さらに低速）
        public float sizeDeadzone = 0.15f; // この量未満のズーム差は無視（常時微調整を止める／寄る方向のみ）
        public float posDeadzone  = 1.6f;  // この量未満の中心移動は無視（小さな動きで動かさない）

        Camera  _cam;
        float   _defaultSize;
        Vector3 _defaultPos;
        Vector3 _prevShake;

        // 背景スプライトのワールド境界（この範囲外＝黒い空間は映さない）。
        bool  _hasBounds;
        float _bgMinX, _bgMaxX, _bgMinY, _bgMaxY;

        // 追従の「保持目標」。デッドゾーン内では更新せず、カメラを静止させる。
        bool    _hasTarget;
        float   _targetSize;
        Vector3 _targetPos;

        void Awake()
        {
            _cam         = GetComponent<Camera>();
            _defaultSize = _cam.orthographicSize;
            _defaultPos  = transform.position;
            _targetSize  = _defaultSize;
            _targetPos   = _defaultPos;
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

            // 全員＋余白が収まる必要サイズ。デフォルトより引かず、寄りすぎもしない。
            float needHalfW = (maxX - minX) * 0.5f + paddingX;
            float needHalfH = (maxY - minY) * 0.5f + paddingY;
            float needSize = Mathf.Max(needHalfW / aspect, needHalfH, _defaultSize * minZoomRatio);
            needSize = Mathf.Min(needSize, _defaultSize);

            // 注視中心を背景内にクランプ（黒い空間を映さない）。
            float halfW = needSize * aspect;
            float needCx = (minX + maxX) * 0.5f;
            // 縦位置は既定の高さに固定する（平坦ステージ前提／画面酔い防止／キャラの立ち位置を
            // 画面の一定の高さに保つ）。上への吹っ飛びは場外インジケータで補う。
            float needCy = _defaultPos.y;
            if (_hasBounds)
            {
                needCx = ClampCenter(needCx, halfW, _bgMinX, _bgMaxX, _defaultPos.x);
            }
            else
            {
                float maxCamX = Mathf.Max(0f, bm.stageHalfWidth + 1.5f - halfW);
                needCx = Mathf.Clamp(needCx, -maxCamX, maxCamX);
            }
            Vector3 needPos = new Vector3(needCx, needCy, _defaultPos.z);

            // 保持目標の更新（デッドゾーン）。
            // ズーム：見切れ防止のため「引く（needSize増）」方向は即追従、
            //         「寄る（needSize減）」方向はデッドゾーンを超えた時だけ更新。
            // 位置：合計移動量がデッドゾーンを超えた時のみ更新。
            if (!_hasTarget)
            {
                _targetSize = needSize;
                _targetPos  = needPos;
                _hasTarget  = true;
            }
            else
            {
                if (needSize > _targetSize || _targetSize - needSize > sizeDeadzone)
                    _targetSize = needSize;

                float posDiff = Mathf.Abs(needPos.x - _targetPos.x) + Mathf.Abs(needPos.y - _targetPos.y);
                if (posDiff > posDeadzone)
                    _targetPos = needPos;
            }

            // 保持目標へ超低速で補間（位置・ズームを分離）。
            float kPos  = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            float kZoom = 1f - Mathf.Exp(-zoomSpeed   * Time.deltaTime);
            _cam.orthographicSize = Mathf.Lerp(_cam.orthographicSize, _targetSize, kZoom);
            transform.position = Vector3.Lerp(transform.position, _targetPos, kPos);
        }

        // 指定した位置・サイズのカメラ表示範囲が背景スプライト内に収まるようクランプした位置を返す。
        // KOズームなど BattleCamera の追従が止まる演出から呼ぶ用。
        public Vector3 ClampToStageBounds(Vector3 pos, float orthoSize)
        {
            if (!_hasBounds) return pos;
            float halfW = orthoSize * Mathf.Max(0.1f, _cam.aspect);
            float halfH = orthoSize;
            float cx = ClampCenter(pos.x, halfW, _bgMinX, _bgMaxX, _defaultPos.x);
            float cy = ClampCenter(pos.y, halfH, _bgMinY, _bgMaxY, _defaultPos.y);
            return new Vector3(cx, cy, pos.z);
        }

        // 表示半幅 half の枠が [min,max] 内に収まる中心値を返す。
        // 枠が範囲より大きい場合は範囲中心へ寄せる。
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
