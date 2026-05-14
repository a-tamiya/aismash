using System.Collections.Generic;
using PromptFighters.Battle.Skills;

namespace PromptFighters.GameFlow
{
    public static class PromptCharacterFactory
    {
        public static CharacterData Create(string characterName, string features, CharacterData fallback)
        {
            if (string.IsNullOrWhiteSpace(characterName) && string.IsNullOrWhiteSpace(features))
                return Clone(fallback);

            string safeName = string.IsNullOrWhiteSpace(characterName)
                ? "Prompt Fighter"
                : characterName.Trim();
            string safeFeatures = string.IsNullOrWhiteSpace(features)
                ? "Balanced fighter with close and ranged attacks."
                : features.Trim();
            Element element = InferElement(safeFeatures);

            var data = new CharacterData
            {
                characterName = safeName,
                inputFeatures = safeFeatures,
                visualPrompt = BuildVisualPrompt(safeName, safeFeatures, element),
                visualDescription = safeFeatures,
                stats = InferStats(safeFeatures),
                spritePath = fallback != null ? fallback.spritePath : "Sprites/test.jpg",
                characterSprite = fallback?.characterSprite,
            };

            data.skills[(int)SkillSlot.AttackA] = BuildCloseSkill(safeName, element);
            data.skills[(int)SkillSlot.AttackB] = BuildRangedSkill(safeName, element);
            data.skills[(int)SkillSlot.AttackC] = BuildSpecialSkill(safeName, element);
            data.skills[(int)SkillSlot.SmashSide] = BuildUltimateSkill(safeName, element);
            return data;
        }

        public static CharacterData Clone(CharacterData src)
        {
            if (src == null)
            {
                var fallback = new CharacterData { characterName = "Sample Fighter" };
                SampleSkillLibrary.EquipDefaults(fallback);
                return fallback;
            }

            var clone = new CharacterData
            {
                characterName = src.characterName,
                inputFeatures = src.inputFeatures,
                visualPrompt = src.visualPrompt,
                visualDescription = src.visualDescription,
                spritePath = src.spritePath,
                spriteDir = src.spriteDir,
                characterSprite = src.characterSprite,
                spriteSet = CloneSpriteSet(src.spriteSet),
                stats = new CharacterStats
                {
                    groundMoveSpeed = src.stats.groundMoveSpeed,
                    airMoveSpeed = src.stats.airMoveSpeed,
                    jumpForce = src.stats.jumpForce,
                    guardDurability = src.stats.guardDurability,
                    lightness = src.stats.lightness,
                    weight = src.stats.weight,
                },
                grabParameters = new GrabParameters
                {
                    range = src.grabParameters.range,
                    startup = src.grabParameters.startup,
                    recovery = src.grabParameters.recovery,
                },
                throwParameters = new ThrowParameters
                {
                    front_damage = src.throwParameters.front_damage,
                    front_knockback = src.throwParameters.front_knockback,
                    back_damage = src.throwParameters.back_damage,
                    back_knockback = src.throwParameters.back_knockback,
                },
            };

            for (int i = 0; i < clone.skills.Length && i < src.skills.Length; i++)
                clone.skills[i] = CloneSkill(src.skills[i]);
            return clone;
        }

        static CharacterSpriteSet CloneSpriteSet(CharacterSpriteSet src)
        {
            var clone = new CharacterSpriteSet();
            if (src?.sprites == null) return clone;
            for (int i = 0; i < clone.sprites.Length && i < src.sprites.Length; i++)
                clone.sprites[i] = src.sprites[i];
            return clone;
        }

        static Element InferElement(string features)
        {
            string text = features.ToLowerInvariant();
            if (ContainsAny(text, "fire", "flame", "burn", "炎", "火", "燃")) return Element.Fire;
            if (ContainsAny(text, "ice", "frost", "snow", "氷", "雪", "冷")) return Element.Ice;
            if (ContainsAny(text, "lightning", "thunder", "雷", "電", "稲妻")) return Element.Lightning;
            if (ContainsAny(text, "dark", "shadow", "闇", "影", "黒")) return Element.Dark;
            if (ContainsAny(text, "wind", "storm", "風", "嵐")) return Element.Wind;
            return Element.Physical;
        }

        static bool ContainsAny(string text, params string[] words)
        {
            for (int i = 0; i < words.Length; i++)
                if (text.Contains(words[i])) return true;
            return false;
        }

        static CharacterStats InferStats(string features)
        {
            string text = features.ToLowerInvariant();
            var stats = new CharacterStats();

            if (ContainsAny(text, "fast", "quick", "speed", "agile", "ninja", "素早", "高速", "俊敏", "軽快", "忍者"))
            {
                stats.groundMoveSpeed = 6.5f;
                stats.airMoveSpeed = 5.4f;
                stats.jumpForce = 13.5f;
                stats.guardDurability = 85f;
                stats.lightness = 1.3f;
                stats.weight = 0.75f;
            }
            else if (ContainsAny(text, "heavy", "giant", "large", "armor", "tank", "重", "大型", "巨", "鎧", "頑丈"))
            {
                stats.groundMoveSpeed = 3.8f;
                stats.airMoveSpeed = 3.0f;
                stats.jumpForce = 9.5f;
                stats.guardDurability = 125f;
                stats.lightness = 0.75f;
                stats.weight = 1.35f;
            }
            else if (ContainsAny(text, "flying", "bird", "wind", "浮", "飛", "鳥", "風"))
            {
                stats.groundMoveSpeed = 5.2f;
                stats.airMoveSpeed = 6.0f;
                stats.jumpForce = 14.5f;
                stats.guardDurability = 90f;
                stats.lightness = 1.18f;
                stats.weight = 0.85f;
            }

            return stats;
        }

        static string BuildVisualPrompt(string name, string features, Element element)
        {
            return $"2D anime standing character, full body, {name}, {features}, {ElementLabel(element)} style";
        }

        static SkillData BuildCloseSkill(string name, Element element)
        {
            var skill = SampleSkillLibrary.CloseSlash();
            skill.skill_name = $"{ElementLabel(element)} Rush";
            skill.description = $"{name} closes the distance and strikes in a quick combo.";
            skill.element = element;
            return skill;
        }

        static SkillData BuildRangedSkill(string name, Element element)
        {
            var skill = SampleSkillLibrary.RangedFireball();
            skill.skill_name = $"{ElementLabel(element)} Shot";
            skill.description = $"{name} fires a ranged attack shaped by the prompt.";
            skill.element = element;
            return skill;
        }

        static SkillData BuildSpecialSkill(string name, Element element)
        {
            var skill = SampleSkillLibrary.SpecialDashStun();
            skill.skill_name = $"{ElementLabel(element)} Step";
            skill.description = $"{name} bursts forward and disrupts the opponent.";
            skill.element = element;
            return skill;
        }

        static SkillData BuildUltimateSkill(string name, Element element)
        {
            var skill = SampleSkillLibrary.UltimateFinisher();
            skill.skill_name = $"{ElementLabel(element)} Finish";
            skill.description = $"{name} releases a high-power finisher based on the prompt.";
            skill.element = element;
            return skill;
        }

        static SkillData CloneSkill(SkillData src)
        {
            if (src == null) return null;

            var clone = new SkillData
            {
                slot = src.slot,
                skill_name = src.skill_name,
                description = src.description,
                element = src.element,
                risk_level = src.risk_level,
                parameters = new SkillParameters
                {
                    damage = src.parameters.damage,
                    range = src.parameters.range,
                    startup = src.parameters.startup,
                    active_time = src.parameters.active_time,
                    recovery = src.parameters.recovery,
                    hit_count = src.parameters.hit_count,
                    knockback = src.parameters.knockback,
                    stun_time = src.parameters.stun_time,
                    guard_damage = src.parameters.guard_damage,
                    move_force = src.parameters.move_force,
                },
                actions = new List<SkillAction>(),
            };

            for (int i = 0; i < src.actions.Count; i++)
            {
                var action = src.actions[i];
                clone.actions.Add(new SkillAction
                {
                    type = action.type,
                    time = action.time,
                    duration = action.duration,
                    range = action.range,
                    hit_count = action.hit_count,
                    damage_override = action.damage_override,
                    power = action.power,
                    direction = action.direction,
                    projectile_speed = action.projectile_speed,
                    projectile_lifetime = action.projectile_lifetime,
                    status = action.status,
                    chance = action.chance,
                });
            }

            return clone;
        }

        static string ElementLabel(Element element) => element switch
        {
            Element.Fire => "Fire",
            Element.Ice => "Ice",
            Element.Lightning => "Lightning",
            Element.Dark => "Dark",
            Element.Wind => "Wind",
            _ => "Power",
        };
    }
}
