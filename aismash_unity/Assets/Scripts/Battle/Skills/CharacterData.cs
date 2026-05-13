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
        public GrabParameters grabParameters = new GrabParameters();
        public ThrowParameters throwParameters = new ThrowParameters();

        public string spritePath        = "Sprites/test.jpg"; // StreamingAssets相対パス or 絶対パス
        public Sprite characterSprite;  // Phase 4で設定（またはspritePath読み込み後に格納）

        public SkillData GetSkill(SkillSlot slot) => skills[(int)slot];
    }

    [System.Serializable]
    public class GrabParameters
    {
        public float range = 1.5f;
        public float startup = 0.12f;
        public float recovery = 1.0f;
    }

    [System.Serializable]
    public class ThrowParameters
    {
        public float front_damage = 10f;
        public float front_knockback = 8f;
        public float back_damage = 10f;
        public float back_knockback = 10f;
    }
}
