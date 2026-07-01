using UnityEngine;

namespace PromptFighters.Battle
{
    // 選択中のステージ定義をシーンに反映する（背景差し替え・stageHalfWidth更新・カメラ境界再キャッシュ）。
    public static class StageManager
    {
        // 現在のスポーンY→Groundオブジェクト中心Y の固定オフセット（-3.5 - (-2.3) = -1.2）
        const float GroundCenterOffset = -1.2f;

        public static void Apply(BattleManager bm)
        {
            var def = StageRegistry.Current;

            // 背景スプライトを差し替え（画像が未生成の場合はそのまま）
            var bgGo = GameObject.Find("StageBackground");
            if (bgGo != null)
            {
                var sr = bgGo.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    var sprite = Resources.Load<Sprite>(def.backgroundPath);
                    if (sprite != null) sr.sprite = sprite;
                }
            }

            // ステージ幅を更新（カメラクランプ・AI ターン判定に影響）
            bm.stageHalfWidth = def.stageHalfWidth;

            // スポーン位置をステージごとに更新
            float sy = def.spawnY;
            bm.fighter1SpawnPos = new Vector3(bm.fighter1SpawnPos.x, sy, bm.fighter1SpawnPos.z);
            bm.fighter2SpawnPos = new Vector3(bm.fighter2SpawnPos.x, sy, bm.fighter2SpawnPos.z);
            bm.bossSpawnPos     = new Vector3(bm.bossSpawnPos.x,     sy, bm.bossSpawnPos.z);

            // 地面コライダをスポーンYに連動して移動（キャラが地面にめり込む・浮くのを防ぐ）
            var groundGo = GameObject.Find("Ground");
            if (groundGo != null)
            {
                var pos = groundGo.transform.position;
                groundGo.transform.position = new Vector3(pos.x, sy + GroundCenterOffset, pos.z);
            }

            // 背景サイズが変わった可能性があるためカメラ境界キャッシュを再計算
            if (Camera.main != null)
                Camera.main.GetComponent<BattleCamera>()?.RefreshStageBounds();
        }
    }
}
