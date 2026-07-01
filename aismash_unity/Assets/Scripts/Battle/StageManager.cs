using UnityEngine;

namespace PromptFighters.Battle
{
    // 選択中のステージ定義をシーンに反映する（背景差し替え・stageHalfWidth更新・カメラ境界再キャッシュ）。
    public static class StageManager
    {
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

            // 背景サイズが変わった可能性があるためカメラ境界キャッシュを再計算
            if (Camera.main != null)
                Camera.main.GetComponent<BattleCamera>()?.RefreshStageBounds();
        }
    }
}
