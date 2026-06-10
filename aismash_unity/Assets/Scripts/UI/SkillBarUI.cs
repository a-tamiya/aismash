using UnityEngine;
using PromptFighters.Battle.Skills;

namespace PromptFighters.UI
{
    // 旧・下部スキルバー。現在は BattleHUD が技スロット表示を担うため何も描画しない。
    // シーン(BattleScene)にコンポーネント参照が残っているため、
    // シリアライズ互換のためフィールドとクラスのみ維持している。
    public class SkillBarUI : MonoBehaviour
    {
        [Header("References")]
        public SkillExecutor skillExecutor;

        [Header("Layout")]
        public bool isLeftSide = true;  // 1P=左, 2P=右

        public void OnCharacterLoaded() { }
    }
}
