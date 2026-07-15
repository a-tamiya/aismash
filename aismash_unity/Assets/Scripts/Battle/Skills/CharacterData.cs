using UnityEngine;

namespace PromptFighters.Battle.Skills
{
    public enum CharacterSpriteId
    {
        Idle1 = 0,
        Idle2 = 1,
        Idle3 = 2,
        Jump = 3,
        Damage = 4,
        Grab = 5,
        Dash = 6,
        AttackA = 7,
        AttackB = 8,
        AttackC = 9,
        SmashSide = 10,
        EffectA = 11,
        EffectB = 12,
        EffectC = 13,
        EffectSmash = 14,
    }

    [System.Serializable]
    public class CharacterSpriteSet
    {
        public Sprite[] sprites = new Sprite[15];

        public Sprite Get(CharacterSpriteId id, Sprite fallback = null, bool fallbackToPrimary = true)
        {
            int index = (int)id;
            if (sprites != null && index >= 0 && index < sprites.Length && sprites[index] != null)
                return sprites[index];
            if (fallbackToPrimary && sprites != null && sprites.Length > 0 && sprites[0] != null)
                return sprites[0];
            return fallback;
        }

        public void Set(CharacterSpriteId id, Sprite sprite)
        {
            if (sprites == null || sprites.Length != 15)
                sprites = new Sprite[15];
            sprites[(int)id] = sprite;
        }
    }

    [System.Serializable]
    public class CharacterVoiceProfile
    {
        public string preset = "coral";
        public string instructions = "日本語の対戦アクションゲームに出演するプロ声優として、キャラクター本人になりきる。棒読みを避け、戦闘中の呼吸、感情の高まり、自然な間、声の強弱を使って臨場感豊かに演じる。";
        public string introLine = "";
        public string[] skillLines = new string[4];
        public bool generated;

        public void FillDefaults(CharacterData owner)
        {
            if (!IsSupportedPreset(preset)) preset = "coral";
            if (string.IsNullOrWhiteSpace(instructions))
                instructions = "日本語の対戦アクションゲームに出演するプロ声優として、キャラクター本人になりきる。棒読みを避け、戦闘中の呼吸、感情の高まり、自然な間、声の強弱を使って臨場感豊かに演じる。";
            if (string.IsNullOrWhiteSpace(introLine))
                introLine = !string.IsNullOrWhiteSpace(owner?.catchCopy)
                    ? owner.catchCopy.Trim()
                    : $"{owner?.characterName ?? "ファイター"}、参上！";

            if (skillLines == null || skillLines.Length != 4)
            {
                var resized = new string[4];
                if (skillLines != null)
                    System.Array.Copy(skillLines, resized, System.Math.Min(skillLines.Length, resized.Length));
                skillLines = resized;
            }

            for (int i = 0; i < skillLines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(skillLines[i])) continue;
                string skillName = owner?.skills != null && i < owner.skills.Length
                    ? owner.skills[i]?.skill_name
                    : null;
                skillLines[i] = string.IsNullOrWhiteSpace(skillName) ? "いくぞ！" : skillName;
            }
        }

        public static bool IsSupportedPreset(string value)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "alloy": case "ash": case "coral": case "echo": case "fable":
                case "onyx": case "nova": case "sage": case "shimmer":
                    return true;
                default:
                    return false;
            }
        }
    }

    // キャラクター1人分のランタイムデータ。
    // Phase 4でAI生成結果がここに入り、Phase 5でファイル保存される。
    public class CharacterData
    {
        public string characterName      = "???";
        public string inputFeatures      = "";
        public string visualPrompt       = "";
        public string visualDescription  = "";
        public string catchCopy          = ""; // AIキャッチコピー
        public CharacterVoiceProfile voiceProfile = new CharacterVoiceProfile();
        public string voiceDir = null; // 保存済みキャラボイスWAVのディレクトリ（絶対パス）

        public SkillData[] skills = new SkillData[4]; // index = SkillSlot
        // ボス専用の追加技プール（4枠システムとは独立。数に制限なし）と、その専用ポーズ/エフェクト画像。
        // 通常のプレイアブルキャラは空リストのまま（無変更・無コスト）。
        public System.Collections.Generic.List<SkillData> extraSkills = new System.Collections.Generic.List<SkillData>();
        public System.Collections.Generic.List<Sprite> extraPoseSprites = new System.Collections.Generic.List<Sprite>();
        public System.Collections.Generic.List<Sprite> extraEffectSprites = new System.Collections.Generic.List<Sprite>();
        public CharacterStats stats = new CharacterStats();
        public GrabParameters grabParameters = new GrabParameters();
        public ThrowParameters throwParameters = new ThrowParameters();

        public float sizeScale = 1f; // 0.7 ~ 1.3: キャラの見た目サイズ倍率

        public string spritePath        = ""; // StreamingAssets相対パス or 絶対パス
        public string spriteDir         = null;               // 保存済みスプライトのディレクトリ（絶対パス）
        public Sprite characterSprite;  // Phase 4で設定（またはspritePath読み込み後に格納）
        public CharacterSpriteSet spriteSet = new CharacterSpriteSet();

        public SkillData GetSkill(SkillSlot slot) => skills[(int)slot];

        public void SetPrimarySprite(Sprite sprite)
        {
            characterSprite = sprite;
            spriteSet.Set(CharacterSpriteId.Idle1, sprite);
        }
    }

    [System.Serializable]
    public class CharacterStats
    {
        public float maxHP = 300f;          // 基準300。AI推論で±50（250〜350）の幅を持つ
        public float groundMoveSpeed = 5f;
        public float airMoveSpeed = 4f;
        public float jumpForce = 12f;
        public float airJumpHeightMultiplier = 0.45f;
        public float walkSpeedRatio = 0.35f;
        public float guardDurability = 65f;
        public float lightness = 1f;
        public float weight = 1f;
        public float groundDodgeDistance = 2.2f;
        public float airDodgeDistance = 1.8f;
    }

    [System.Serializable]
    public class GrabParameters
    {
        public float range = 1.5f;
        public float startup = 0.08f;
        public float recovery = 0.14f;
    }

    [System.Serializable]
    public class ThrowParameters
    {
        public float front_damage = 10f;
        public float front_knockback = 8f;
        public float back_damage = 10f;
        public float back_knockback = 10f;
        public float up_damage = 10f;
        public float up_knockback = 9f;
        public float down_damage = 8f;
        public float down_knockback = 7f;
    }
}
