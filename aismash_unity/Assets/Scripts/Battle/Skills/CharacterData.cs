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
        public const int CurrentQualityVersion = 2;
        public const string Male = "male";
        public const string Female = "female";
        public const string Neutral = "neutral";
        public const string DefaultActingInstructions =
            "日本語の対戦アクションゲームに出演するプロ声優として、キャラクター本人になりきる。" +
            "棒読みを避け、戦闘中の呼吸、感情の高まり、自然な間、声の強弱を使って臨場感豊かに演じる。";

        // presetはAIの自由選択を採用せず、voiceGenderからFillDefaultsで決定する。
        // cedar/marinは高品質な声、alloyは性別を限定しないキャラに使用する。
        public string preset = "alloy";
        public string voiceGender = "unspecified";
        public string voiceAge = "unspecified";
        public string voicePitch = "unspecified";
        public int qualityVersion;
        public string instructions = DefaultActingInstructions;
        public string introLine = "";
        public string[] skillLines = new string[4];
        public bool generated;

        static readonly string[] MaleHints =
        {
            "男性", "男の子", "少年", "青年", "男子", "男キャラ", "男のキャラ", "男声", "男の声", "男らしい",
            "お兄さん", "兄貴", "おじさん", "お爺", "老爺", "male", "boy", "gentleman"
        };

        static readonly string[] FemaleHints =
        {
            "女性", "女の子", "少女", "女子", "女キャラ", "女のキャラ", "女声", "女の声", "女らしい",
            "お姉さん", "おばさん", "老婆", "female", "woman", "girl", "lady"
        };

        public void FillDefaults(CharacterData owner)
        {
            // 新JSONの構造化値を優先する。旧JSONで欠損した場合だけ、音声指示→入力特徴→外見説明の順に補完する。
            // 特徴には召喚対象や攻撃名も含まれるため、単語推論で有効な構造化値を上書きしない。
            string normalizedGender = NormalizeGender(voiceGender);
            if (normalizedGender != "unspecified")
                voiceGender = normalizedGender;
            else
            {
                // 旧JSONではまずユーザー原文の明示宣言を採用し、なければ保存済み自由記述から推定する。
                if (!ApplyExplicitGenderFromInput(owner?.inputFeatures))
                {
                    voiceGender = InferGender(instructions);
                    if (voiceGender == "unspecified") voiceGender = InferGender(owner?.inputFeatures);
                    if (voiceGender == "unspecified") voiceGender = InferGender(owner?.visualDescription);
                    if (voiceGender == "unspecified") voiceGender = Neutral;
                }
            }

            voiceAge = NormalizeAge(voiceAge);
            if (voiceAge == null)
                voiceAge = InferAge(instructions) ?? InferAge(owner?.inputFeatures) ??
                    InferAge(owner?.visualDescription) ?? "adult";

            voicePitch = NormalizePitch(voicePitch);
            if (voicePitch == null)
                voicePitch = InferPitch(instructions) ?? InferPitch(owner?.inputFeatures) ??
                    InferPitch(owner?.visualDescription) ?? "medium";

            // LLMが返したpresetや旧保存データのpresetに左右されず、性別を全台詞で固定する。
            preset = ResolvePreset(voiceGender);
            if (string.IsNullOrWhiteSpace(instructions))
                instructions = DefaultActingInstructions;
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

        public string BuildIdentityInstruction()
        {
            string genderDirection = voiceGender == Male
                ? "声の核は明確な男性。男性の声の響きと共鳴を終始保ち、女性の声や女性的な声色にはしない。"
                : voiceGender == Female
                    ? "声の核は明確な女性。女性の声の響きと共鳴を終始保ち、男性の声や男性的な声色にはしない。"
                    : "声の核は中性的。男性または女性のどちらかへ極端に寄せず、同じ中性的な声色を保つ。";
            string ageDirection = voiceAge switch
            {
                "child" => "子どもの年齢感",
                "teen" => "10代の年齢感",
                "young_adult" => "若い成人の年齢感",
                "senior" => "高齢者の年齢感",
                "ageless" => "年齢を特定しにくい超然とした年齢感",
                _ => "成人の年齢感",
            };
            string pitchDirection = voicePitch switch
            {
                "low" => "低めの基本ピッチ",
                "high" => "高めの基本ピッチ",
                _ => "中程度の基本ピッチ",
            };
            return $"音声アイデンティティ（最優先）: {genderDirection}{ageDirection}、{pitchDirection}。" +
                   "感情が高ぶってもこの性別・年齢感・声の高さを崩さず、全台詞を同一人物の声で演じる。" +
                   "それ以前の自由記述に性別・年齢・声の高さの矛盾があっても、必ずこの音声アイデンティティを優先する。";
        }

        // AIの推測より、ユーザーが特徴欄で明示した本人の性別を優先する。
        // 「女性を守る」「女神を召喚」のような他者への言及を誤採用しないよう、宣言形だけに限定する。
        public bool ApplyExplicitGenderFromInput(string text)
        {
            bool male = ContainsAny(text,
                "男性キャラ", "男性キャラクター", "男キャラ", "男のキャラ", "男のキャラクター",
                "男性の声", "男の声", "男声", "性別は男性", "性別：男性", "性別:男性",
                "性別は男", "性別：男", "性別:男", "male character", "male voice", "gender: male", "gender=male");
            bool female = ContainsAny(text,
                "女性キャラ", "女性キャラクター", "女キャラ", "女のキャラ", "女のキャラクター",
                "女性の声", "女の声", "女声", "性別は女性", "性別：女性", "性別:女性",
                "性別は女", "性別：女", "性別:女", "female character", "female voice", "gender: female", "gender=female");
            bool neutral = ContainsAny(text,
                "中性キャラ", "中性的なキャラ", "中性的な声", "中性の声", "性別は中性", "性別：中性", "性別:中性",
                "性別なし", "無性別", "neutral character", "neutral voice", "gender: neutral", "gender=neutral");
            int matches = (male ? 1 : 0) + (female ? 1 : 0) + (neutral ? 1 : 0);
            if (matches != 1) return false;

            voiceGender = male ? Male : female ? Female : Neutral;
            preset = ResolvePreset(voiceGender);
            return true;
        }

        public static string ResolvePreset(string gender)
        {
            switch (NormalizeGender(gender))
            {
                case Male: return "cedar";
                case Female: return "marin";
                default: return "alloy";
            }
        }

        static string NormalizeGender(string value)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "male": case "man": case "masculine": case "男性": case "男": return Male;
                case "female": case "woman": case "feminine": case "女性": case "女": return Female;
                case "neutral": case "nonbinary": case "non-binary": case "agender":
                case "genderless": case "中性": case "無性": return Neutral;
                default: return "unspecified";
            }
        }

        static string NormalizeAge(string value)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "child": case "kid": case "子供": case "子ども": return "child";
                case "teen": case "teenager": case "少年": case "少女": case "10代": return "teen";
                case "young_adult": case "young-adult": case "young adult": case "青年": return "young_adult";
                case "adult": case "大人": case "成人": return "adult";
                case "senior": case "elder": case "elderly": case "老人": case "高齢者": return "senior";
                case "ageless": case "年齢不詳": case "不老": return "ageless";
                default: return null;
            }
        }

        static string NormalizePitch(string value)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "low": case "低": case "低い": case "低音": return "low";
                case "medium": case "mid": case "normal": case "中": case "普通": return "medium";
                case "high": case "高": case "高い": case "高音": return "high";
                default: return null;
            }
        }

        static string InferGender(string text)
        {
            int maleScore = CountHints(text, MaleHints);
            int femaleScore = CountHints(text, FemaleHints);
            if (maleScore == femaleScore) return "unspecified";
            return maleScore > femaleScore ? Male : Female;
        }

        static string InferAge(string text)
        {
            bool child = ContainsAny(text, "幼児", "子供", "子ども", "児童", "child", "kid");
            bool teen = ContainsAny(text, "少年", "少女", "高校生", "中学生", "10代", "teen");
            bool youngAdult = ContainsAny(text, "青年", "若者", "若い大人", "大学生", "young adult");
            bool senior = ContainsAny(text, "老人", "老爺", "老婆", "高齢", "senior", "elder");
            bool ageless = ContainsAny(text, "年齢不詳", "不老", "ageless");
            bool adult = ContainsAny(text, "成人", "大人", "adult");
            int matches = (child ? 1 : 0) + (teen ? 1 : 0) + (youngAdult ? 1 : 0) +
                          (senior ? 1 : 0) + (ageless ? 1 : 0) + (adult ? 1 : 0);
            if (matches != 1) return null;
            if (child) return "child";
            if (teen) return "teen";
            if (youngAdult) return "young_adult";
            if (senior) return "senior";
            if (ageless) return "ageless";
            return "adult";
        }

        static string InferPitch(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            bool low = ContainsAny(text, "重低音の声", "低音ボイス", "低い声", "低めの声", "low pitch", "deep voice") ||
                System.Text.RegularExpressions.Regex.IsMatch(text, "低音(?!波)");
            bool high = ContainsAny(text, "高音ボイス", "高い声", "高めの声", "甲高い声", "high pitch", "high-pitched") ||
                System.Text.RegularExpressions.Regex.IsMatch(text, "高音(?!波)");
            if (low == high) return null;
            return low ? "low" : "high";
        }

        static int CountHints(string text, string[] hints)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int count = 0;
            foreach (string hint in hints)
            {
                bool asciiWord = true;
                foreach (char c in hint)
                    if (c > 127 || (!char.IsLetter(c) && c != '-' && c != ' ')) { asciiWord = false; break; }
                bool found = asciiWord
                    ? System.Text.RegularExpressions.Regex.IsMatch(text,
                        $@"(?<![A-Za-z]){System.Text.RegularExpressions.Regex.Escape(hint)}(?![A-Za-z])",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    : text.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (found) count++;
            }
            return count;
        }

        static bool ContainsAny(string text, params string[] hints) => CountHints(text, hints) > 0;

        public static bool IsSupportedPreset(string value)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "alloy": case "ash": case "coral": case "echo": case "fable":
                case "onyx": case "nova": case "sage": case "shimmer": case "ballad":
                case "verse": case "marin": case "cedar":
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
