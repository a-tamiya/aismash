using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PromptFighters.Battle.Skills;
using PromptFighters.Battle.Skills.Json;

namespace PromptFighters.GameFlow
{
    // 生成されたキャラクターをpersistentDataPathに保存・読み込みする。
    public static class CharacterSaveManager
    {
        static string SaveDir => Path.Combine(Application.persistentDataPath, "SavedChars");

        public static void Save(CharacterData data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.characterName)) return;
            try
            {
                Directory.CreateDirectory(SaveDir);
                string id   = SanitizeId(data.characterName) + "_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string path = Path.Combine(SaveDir, id + ".json");
                string json = Serialize(data);
                File.WriteAllText(path, json, Encoding.UTF8);
                PresetCharacterLoader.ClearCache();
                Debug.Log($"[Save] 保存完了: {path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] 保存失敗: {e.Message}");
            }
        }

        public static List<CharacterData> LoadAll()
        {
            var results = new List<CharacterData>();
            if (!Directory.Exists(SaveDir)) return results;

            foreach (var path in Directory.GetFiles(SaveDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var data    = SkillJsonParser.Parse(json);
                    if (data != null) results.Add(data);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Save] 読み込み失敗 ({Path.GetFileName(path)}): {e.Message}");
                }
            }
            return results;
        }

        // CharacterData → JSONテキスト（SkillJsonParser.Parse で再読み込み可能な形式）
        static string Serialize(CharacterData d)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"character_name\": {Q(d.characterName)},");
            sb.AppendLine($"  \"input_features\": {Q(d.inputFeatures)},");
            sb.AppendLine($"  \"visual_prompt\": {Q(d.visualPrompt)},");
            sb.AppendLine($"  \"visual_description\": {Q(d.visualDescription)},");
            sb.AppendLine("  \"skills\": [");

            bool firstSkill = true;
            foreach (var skill in d.skills)
            {
                if (skill == null) continue;
                if (!firstSkill) sb.AppendLine(",");
                firstSkill = false;
                AppendSkill(sb, skill);
            }
            sb.AppendLine();
            sb.AppendLine("  ]");
            sb.Append("}");
            return sb.ToString();
        }

        static void AppendSkill(StringBuilder sb, SkillData s)
        {
            var p = s.parameters;
            sb.AppendLine("    {");
            sb.AppendLine($"      \"slot\": {Q(SlotStr(s.slot))},");
            sb.AppendLine($"      \"skill_name\": {Q(s.skill_name)},");
            sb.AppendLine($"      \"description\": {Q(s.description)},");
            sb.AppendLine($"      \"element\": {Q(ElemStr(s.element))},");
            sb.AppendLine($"      \"risk_level\": {Q(RiskStr(s.risk_level))},");
            sb.AppendLine($"      \"parameters\": {{");
            sb.AppendLine($"        \"damage\": {p.damage}, \"hit_count\": {p.hit_count}, \"range\": {p.range},");
            sb.AppendLine($"        \"startup\": {p.startup}, \"active_time\": {p.active_time}, \"recovery\": {p.recovery},");
            sb.AppendLine($"        \"cooldown\": {p.cooldown}, \"knockback\": {p.knockback}, \"stun_time\": {p.stun_time},");
            sb.AppendLine($"        \"guard_damage\": {p.guard_damage}, \"move_force\": {p.move_force}");
            sb.AppendLine("      },");
            sb.AppendLine("      \"actions\": [");

            bool first = true;
            foreach (var a in s.actions)
            {
                if (!first) sb.AppendLine(",");
                first = false;
                AppendAction(sb, a);
            }
            sb.AppendLine();
            sb.AppendLine("      ]");
            sb.Append("    }");
        }

        static void AppendAction(StringBuilder sb, SkillAction a)
        {
            sb.Append($"        {{\"type\":{Q(a.type)},\"time\":{a.time}");
            if (!string.IsNullOrEmpty(a.direction)) sb.Append($",\"direction\":{Q(a.direction)}");
            if (a.power > 0f)                        sb.Append($",\"power\":{a.power}");
            if (a.range > 0f)                        sb.Append($",\"range\":{a.range}");
            if (a.hit_count > 0)                     sb.Append($",\"hit_count\":{a.hit_count}");
            if (a.projectile_speed > 0f)             sb.Append($",\"projectile_speed\":{a.projectile_speed}");
            if (a.projectile_lifetime > 0f)          sb.Append($",\"projectile_lifetime\":{a.projectile_lifetime}");
            if (!string.IsNullOrEmpty(a.status))     sb.Append($",\"status\":{Q(a.status)},\"duration\":{a.duration},\"chance\":{a.chance}");
            if (a.damage_override >= 0f)             sb.Append($",\"damage_override\":{a.damage_override}");
            sb.Append("}");
        }

        static string Q(string s) => s == null ? "\"\"" : $"\"{s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n")}\"";

        static string SlotStr(SkillSlot s) => s switch {
            SkillSlot.Close    => "close",
            SkillSlot.Ranged   => "ranged",
            SkillSlot.Special  => "special",
            SkillSlot.Ultimate => "ultimate",
            _                  => "close",
        };
        static string ElemStr(Element e) => e switch {
            Element.Physical  => "physical",
            Element.Fire      => "fire",
            Element.Ice       => "ice",
            Element.Lightning => "lightning",
            Element.Dark      => "dark",
            Element.Wind      => "wind",
            _                 => "none",
        };
        static string RiskStr(RiskLevel r) => r switch {
            RiskLevel.Low     => "low",
            RiskLevel.Medium  => "medium",
            RiskLevel.High    => "high",
            RiskLevel.Extreme => "extreme",
            _                 => "medium",
        };

        static string SanitizeId(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "char";
        }
    }
}
