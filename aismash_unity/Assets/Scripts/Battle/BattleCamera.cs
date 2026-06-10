using UnityEngine;

namespace PromptFighters.Battle
{
    // バトルカメラ。生存ファイター全員（1v1は2人、協力はボス含む）が
    // 画面に収まるよう、距離に応じて自動ズーム・追従する。
    // カメラ揺れ（CameraShake）の適用もここで行い、カメラ制御を一本化する。
    // KOスロー中は BattleManager の KO ズーム演出を優先して追従を止める。
    [RequireComponent(typeof(Camera))]
    public class BattleCamera : MonoBehaviour
    {
        [Range(0.4f, 1f)] public float minZoomRatio = 0.62f; // 最大ズームイン時のサイズ比（デフォルト比）
        public float paddingX    = 2.8f;  // ファイターの外側に確保する横余白（ワールド）
        public float paddingY    = 1.7f;  // 同・縦余白
        public float followSpeed = 4.5f;  // 位置・ズームの追従速度（大きいほど機敏）
        public float stageEdgePad = 1.5f; // ステージ端より外をどこまで見せるか

        Camera  _cam;
        float   _defaultSize;
        Vector3 _defaultPos;
        Vector3 _prevShake;

        void Awake()
        {
            _cam         = GetComponent<Camera>();
            _defaultSize = _cam.orthographicSize;
            _defaultPos  = transform.position;
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

            // 中心X。ズームに応じてステージ端より外が映りすぎないようクランプ。
            float halfW   = size * aspect;
            float maxCamX = Mathf.Max(0f, bm.stageHalfWidth + stageEdgePad - halfW);
            float x = Mathf.Clamp((minX + maxX) * 0.5f, -maxCamX, maxCamX);

            // 高所のファイターが見切れないぶんだけ持ち上げる（地面の framing は維持）。
            float y = Mathf.Max(_defaultPos.y, maxY + paddingY * 0.7f - size);

            float k = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            _cam.orthographicSize = Mathf.Lerp(_cam.orthographicSize, size, k);
            transform.position = Vector3.Lerp(
                transform.position, new Vector3(x, y, _defaultPos.z), k);
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
