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
        public string base_visual_prompt;
        public string visual_prompt;
        public string visual_description;
        public CharacterStats stats = new CharacterStats();
        public List<SkillJsonRaw> skills = new List<SkillJsonRaw>();
        public GrabParameters grab_parameters = new GrabParameters();
        public ThrowParameters throw_parameters = new ThrowParameters();
    }

    [Serializable]
    public class SkillJsonRaw
    {
        public string slot;         // "attack_a" | "attack_b" | "attack_c" | "smash_side"
        public string skill_name;
        public string description;
        public string element;      // "fire" | "ice" | "lightning" | "dark" | "wind" | "physical" | "none"
        public string risk_level;   // "low" | "medium" | "high" | "extreme"
        public SkillParameters parameters = new SkillParameters();
        public List<SkillAction>  actions = new List<SkillAction>();
    }
}
