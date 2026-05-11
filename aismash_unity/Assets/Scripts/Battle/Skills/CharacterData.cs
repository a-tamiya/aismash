using UnityEngine;

namespace PromptFighters.Battle.Skills
{
    // キャラクター1人分のランタイムデータ。
    // Phase 4でAI生成結果がここに入り、Phase 5でファイル保存される。
    public class CharacterData
    {
        public string characterName      = "???";
        public string inputFeatures      = "";
        public string visualPrompt       = "";
        public string visualDescription  = "";

        public SkillData[] skills = new SkillData[4]; // index = SkillSlot

        public Sprite characterSprite;  // Phase 4で設定

        public SkillData GetSkill(SkillSlot slot) => skills[(int)slot];
    }
}
