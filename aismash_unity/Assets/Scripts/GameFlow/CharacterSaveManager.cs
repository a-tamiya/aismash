using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PromptFighters.Battle.Skills;
using PromptFighters.Battle.Skills.Json;
using PromptFighters.Utils;

namespace PromptFighters.GameFlow
{
    // 生成されたキャラクターをpersistentDataPathに保存・読み込みする。
    // スプライトPNGは {SaveDir}/{id}/sprites/ 以下に保存する。
    public static class CharacterSaveManager
    {
        static string SaveDir => Path.Combine(Application.persistentDataPath, "SavedChars");

        static readonly (CharacterSpriteId id, string filename)[] SpriteEntries =
        {
            (CharacterSpriteId.Idle1,      "idle1"),
            (CharacterSpriteId.Idle2,      "idle2"),
            (CharacterSpriteId.Idle3,      "idle3"),
            (CharacterSpriteId.Jump,       "jump"),
            (CharacterSpriteId.Damage,     "damage"),
            (CharacterSpriteId.Grab,       "grab"),
            (CharacterSpriteId.Dash,       "dash"),
            (CharacterSpriteId.AttackA,    "attack_a"),
            (CharacterSpriteId.AttackB,    "attack_b"),
            (CharacterSpriteId.AttackC,    "attack_c"),
            (CharacterSpriteId.SmashSide,  "smash_side"),
            (CharacterSpriteId.EffectA,    "effect_a"),
            (CharacterSpriteId.EffectB,    "effect_b"),
            (CharacterSpriteId.EffectC,    "effect_c"),
            (CharacterSpriteId.EffectSmash,"effect_smash"),
        };

        // JSONを保存し、data.spriteDir を設定する。
        public static void Save(CharacterData data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.characterName)) return;
            try
            {
                Directory.CreateDirectory(SaveDir);
                string id   = SanitizeId(data.characterName) + "_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string path = Path.Combine(SaveDir, id + ".json");
                data.spriteDir = Path.Combine(SaveDir, id, "sprites");
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

        // 透過済みスプライトをPNGとして保存する（AIImageClientが保存済みの場合は不要）
        public static void SaveSprites(CharacterData data)
        {
            if (data?.spriteSet == null || string.IsNullOrEmpty(data.spriteDir)) return;
            try
            {
                Directory.CreateDirectory(data.spriteDir);
                foreach (var (id, filename) in SpriteEntries)
                {
                    var sprite = data.spriteSet.sprites[(int)id];
                    if (sprite?.texture == null) continue;
                    byte[] png = ImageConversion.EncodeToPNG(sprite.texture);
                    File.WriteAllBytes(Path.Combine(data.spriteDir, filename + ".png"), png);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] スプライト保存失敗: {e.Message}");
            }
        }

        // 保存済みキャラを全件ロードする。Idle1プレビュー用スプライトも設定する。
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
                    if (data == null) continue;

                    string id        = Path.GetFileNameWithoutExtension(path);
                    string spriteDir = Path.Combine(SaveDir, id, "sprites");
                    data.spriteDir   = spriteDir;

                    // Idle1をプレビュー用にロード
                    string idle1 = Path.Combine(spriteDir, "idle1.png");
                    if (File.Exists(idle1))
                        data.characterSprite = SpriteLoader.LoadDirect(idle1);

                    results.Add(data);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Save] 読み込み失敗 ({Path.GetFileName(path)}): {e.Message}");
                }
            }
            return results;
        }

        public static bool Delete(CharacterData data)
        {
            if (data == null || string.IsNullOrEmpty(data.spriteDir)) return false;

            try
            {
                string characterDir = Directory.GetParent(data.spriteDir)?.FullName;
                if (string.IsNullOrEmpty(characterDir)) return false;

                string id = Path.GetFileName(characterDir);
                string jsonPath = Path.Combine(SaveDir, id + ".json");
                if (File.Exists(jsonPath))
                    File.Delete(jsonPath);
                if (Directory.Exists(characterDir))
                    Directory.Delete(characterDir, true);

                PresetCharacterLoader.ClearCache();
                Debug.Log($"[Save] 削除完了: {id}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] 削除失敗: {e.Message}");
                return false;
            }
        }

        // バトル開始時に保存済みスプライトセットをフルロードする。
        public static CharacterSpriteSet LoadSpriteSet(string spriteDir)
        {
            if (string.IsNullOrEmpty(spriteDir) || !Directory.Exists(spriteDir)) return null;

            var set = new CharacterSpriteSet();
            bool anyLoaded = false;

            foreach (var (id, filename) in SpriteEntries)
            {
                string path = Path.Combine(spriteDir, filename + ".png");
                if (!File.Exists(path)) continue;
                var sprite = SpriteLoader.LoadDirect(path);
                if (sprite == null) continue;
                set.Set(id, sprite);
                anyLoaded = true;
            }

            return anyLoaded ? set : null;
        }

        static string Serialize(CharacterData d)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"character_name\": {Q(d.characterName)},");
            sb.AppendLine($"  \"input_features\": {Q(d.inputFeatures)},");
            sb.AppendLine($"  \"base_visual_prompt\": {Q(d.visualPrompt)},");
            sb.AppendLine($"  \"visual_description\": {Q(d.visualDescription)},");
            sb.AppendLine($"  \"stats\": {{\"groundMoveSpeed\": {d.stats.groundMoveSpeed}, \"airMoveSpeed\": {d.stats.airMoveSpeed}, \"jumpForce\": {d.stats.jumpForce}, \"guardDurability\": {d.stats.guardDurability}, \"lightness\": {d.stats.lightness}, \"weight\": {d.stats.weight}, \"groundDodgeDistance\": {d.stats.groundDodgeDistance}, \"airDodgeDistance\": {d.stats.airDodgeDistance}}},");
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
            sb.AppendLine("  ],");
            sb.AppendLine($"  \"grab_parameters\": {{\"range\": {d.grabParameters.range}, \"startup\": {d.grabParameters.startup}, \"recovery\": {d.grabParameters.recovery}}},");
            sb.AppendLine($"  \"throw_parameters\": {{\"front_damage\": {d.throwParameters.front_damage}, \"front_knockback\": {d.throwParameters.front_knockback}, \"back_damage\": {d.throwParameters.back_damage}, \"back_knockback\": {d.throwParameters.back_knockback}}}");
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
            sb.AppendLine($"        \"knockback\": {p.knockback}, \"stun_time\": {p.stun_time},");
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
            if (a.spawn_x > 0f)                      sb.Append($",\"spawn_x\":{a.spawn_x}");
            if (!Mathf.Approximately(a.spawn_y, 0f)) sb.Append($",\"spawn_y\":{a.spawn_y}");
            if (a.size_y > 0f)                       sb.Append($",\"size_y\":{a.size_y}");
            if (a.hit_count > 0)                     sb.Append($",\"hit_count\":{a.hit_count}");
            if (a.projectile_speed > 0f)             sb.Append($",\"projectile_speed\":{a.projectile_speed}");
            if (a.projectile_lifetime > 0f)          sb.Append($",\"projectile_lifetime\":{a.projectile_lifetime}");
            if (!string.IsNullOrEmpty(a.status))     sb.Append($",\"status\":{Q(a.status)},\"duration\":{a.duration},\"chance\":{a.chance}");
            if (a.damage_override >= 0f)             sb.Append($",\"damage_override\":{a.damage_override}");
            sb.Append("}");
        }

        static string Q(string s) => s == null ? "\"\"" : $"\"{s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n")}\"";

        static string SlotStr(SkillSlot s) => s switch {
            SkillSlot.AttackA   => "attack_a",
            SkillSlot.AttackB   => "attack_b",
            SkillSlot.AttackC   => "attack_c",
            SkillSlot.SmashSide => "smash_side",
            _                   => "attack_a",
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
