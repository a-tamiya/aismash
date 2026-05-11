using System;
using System.Collections.Generic;

namespace PromptFighters.Battle.Skills.Json
{
    // AI出力JSONと1:1対応する中間クラス。JsonUtilityで直接デシリアライズする。
    // enum相当フィールドは文字列で受け取り、SkillJsonParserで変換する。

    [Serializable]
    public class CharacterJsonRaw
    {
        public string character_name;
        public string input_features;
        public string visual_prompt;
        public string visual_description;
        public List<SkillJsonRaw> skills = new List<SkillJsonRaw>();
    }

    [Serializable]
    public class SkillJsonRaw
    {
        public string slot;         // "close" | "ranged" | "special" | "ultimate"
        public string skill_name;
        public string description;
        public string element;      // "fire" | "ice" | "lightning" | "dark" | "wind" | "physical" | "none"
        public string risk_level;   // "low" | "medium" | "high" | "extreme"
        public SkillParameters parameters = new SkillParameters();
        public List<SkillAction>  actions = new List<SkillAction>();
    }
}
