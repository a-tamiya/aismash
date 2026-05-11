using System.Collections.Generic;
using UnityEngine;

namespace PromptFighters.Battle.Skills.Json
{
    // AI出力JSONを CharacterData に変換するパーサー。
    // 変換に失敗したスロットはSampleSkillLibraryのフォールバック技で補完する（要件15.2）。
    public static class SkillJsonParser
    {
        public static CharacterData Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogWarning("[SkillJsonParser] JSON文字列が空です。フォールバックを使用します。");
                return null;
            }

            CharacterJsonRaw raw;
            try
            {
                raw = JsonUtility.FromJson<CharacterJsonRaw>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SkillJsonParser] JSON解析エラー: {e.Message}");
                return null;
            }

            if (raw == null || string.IsNullOrEmpty(raw.character_name))
            {
                Debug.LogWarning("[SkillJsonParser] 必須フィールド(character_name)が不足しています。");
                return null;
            }

            var data = new CharacterData
            {
                characterName     = raw.character_name,
                inputFeatures     = raw.input_features     ?? "",
                visualPrompt      = raw.visual_prompt      ?? "",
                visualDescription = raw.visual_description ?? "",
            };

            // スキルを変換
            var skillMap = new SkillData[4];
            if (raw.skills != null)
            {
                foreach (var rawSkill in raw.skills)
                {
                    var skill = ConvertSkill(rawSkill);
                    if (skill == null) continue;

                    BalanceCorrector.Apply(skill);
                    int idx = (int)skill.slot;
                    if (idx >= 0 && idx < 4 && skillMap[idx] == null)
                        skillMap[idx] = skill;
                }
            }

            // 欠けているスロットをフォールバック技で補完
            for (int i = 0; i < 4; i++)
            {
                if (skillMap[i] == null)
                {
                    Debug.LogWarning($"[SkillJsonParser] スロット{(SkillSlot)i}が未定義。サンプル技を使用します。");
                    skillMap[i] = SampleSkillLibrary.ForSlot((SkillSlot)i);
                }
            }

            data.skills = skillMap;
            return data;
        }

        // ParseOrFallback: 失敗時はキャラ名だけ入れてサンプル技で構築
        public static CharacterData ParseOrFallback(string json, string fallbackName = "???")
        {
            var result = Parse(json);
            if (result != null) return result;

            var fallback = new CharacterData { characterName = fallbackName };
            SampleSkillLibrary.EquipDefaults(fallback);
            return fallback;
        }

        static SkillData ConvertSkill(SkillJsonRaw raw)
        {
            if (raw == null) return null;

            // 必須フィールドチェック
            if (string.IsNullOrEmpty(raw.slot))
            {
                Debug.LogWarning("[SkillJsonParser] skill.slot が未指定のためスキップ。");
                return null;
            }

            return new SkillData
            {
                slot        = SkillEnumParser.ParseSlot(raw.slot),
                skill_name  = string.IsNullOrEmpty(raw.skill_name) ? "名称不明" : raw.skill_name,
                description = raw.description ?? "",
                element     = SkillEnumParser.ParseElement(raw.element),
                risk_level  = ParseRiskLevel(raw.risk_level),
                parameters  = raw.parameters ?? new SkillParameters(),
                actions     = raw.actions    ?? new List<SkillAction>(),
            };
        }

        static RiskLevel ParseRiskLevel(string s) => s switch
        {
            "low"     => RiskLevel.Low,
            "medium"  => RiskLevel.Medium,
            "high"    => RiskLevel.High,
            "extreme" => RiskLevel.Extreme,
            _         => RiskLevel.Medium,
        };
    }
}
