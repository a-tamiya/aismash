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
                spritePath = fallback?.spritePath ?? "",
                characterSprite = fallback?.characterSprite,
            };

            data.skills[(int)SkillSlot.AttackA] = BuildCloseSkill(safeName, element);
            if (PrefersRanged(safeFeatures) && !PrefersClose(safeFeatures))
            {
                data.skills[(int)SkillSlot.AttackA] = BuildRangedSkill(safeName, element, SkillSlot.AttackA);
                data.skills[(int)SkillSlot.AttackB] = BuildRangedSkill(safeName, element, SkillSlot.AttackB);
                data.skills[(int)SkillSlot.AttackC] = BuildTrapSkill(safeName, element);
            }
            else if (PrefersClose(safeFeatures) && !PrefersRanged(safeFeatures))
            {
                data.skills[(int)SkillSlot.AttackA] = BuildCloseSkill(safeName, element);
                data.skills[(int)SkillSlot.AttackB] = BuildDashSkill(safeName, element);
                data.skills[(int)SkillSlot.AttackC] = BuildAreaSkill(safeName, element);
            }
            else
            {
                data.skills[(int)SkillSlot.AttackA] = BuildCloseSkill(safeName, element);
                data.skills[(int)SkillSlot.AttackB] = BuildRangedSkill(safeName, element, SkillSlot.AttackB);
                data.skills[(int)SkillSlot.AttackC] = BuildTrapSkill(safeName, element);
            }
            data.skills[(int)SkillSlot.SmashSide] = BuildUltimateSkill(safeName, element);
            data.sizeScale = CharacterSizeEstimator.Estimate(data);
            return data;
        }

        public static CharacterData Clone(CharacterData src)
        {
            if (src == null)
            {
                var fallback = new CharacterData { characterName = "電脳の巫女・ミコト" };
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
                    airJumpHeightMultiplier = src.stats.airJumpHeightMultiplier,
                    walkSpeedRatio = src.stats.walkSpeedRatio,
                    guardDurability = src.stats.guardDurability,
                    lightness = src.stats.lightness,
                    weight = src.stats.weight,
                    groundDodgeDistance = src.stats.groundDodgeDistance,
                    airDodgeDistance = src.stats.airDodgeDistance,
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
                    up_damage = src.throwParameters.up_damage,
                    up_knockback = src.throwParameters.up_knockback,
                    down_damage = src.throwParameters.down_damage,
                    down_knockback = src.throwParameters.down_knockback,
                },
            };

            clone.sizeScale = src.sizeScale;
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

        static bool PrefersRanged(string features)
        {
            string text = features.ToLowerInvariant();
            return ContainsAny(text, "ranged", "shoot", "gun", "bow", "magic", "beam", "遠距離", "射撃", "弓", "銃", "砲", "魔法", "ビーム", "弾");
        }

        static bool PrefersClose(string features)
        {
            string text = features.ToLowerInvariant();
            return ContainsAny(text, "close", "melee", "sword", "punch", "kick", "slash", "近距離", "接近", "剣", "刀", "殴", "蹴", "斬", "格闘");
        }

        static CharacterStats InferStats(string features)
        {
            string text = features.ToLowerInvariant();
            var stats = new CharacterStats();

            if (ContainsAny(text, "fast", "quick", "speed", "agile", "ninja", "素早", "高速", "俊敏", "軽快", "忍者"))
            {
                stats.groundMoveSpeed = 8.2f;
                stats.airMoveSpeed = 7.2f;
                stats.jumpForce = 16.5f;
                stats.airJumpHeightMultiplier = 0.55f;
                stats.walkSpeedRatio = 0.45f;
                stats.guardDurability = 55f;
                stats.lightness = 1.65f;
                stats.weight = 0.6f;
                stats.groundDodgeDistance = 3.2f;
                stats.airDodgeDistance = 2.7f;
            }
            else if (ContainsAny(text, "heavy", "giant", "large", "armor", "tank", "重", "大型", "巨", "鎧", "頑丈"))
            {
                stats.groundMoveSpeed = 2.9f;
                stats.airMoveSpeed = 2.3f;
                stats.jumpForce = 8.0f;
                stats.airJumpHeightMultiplier = 0.32f;
                stats.walkSpeedRatio = 0.25f;
                stats.guardDurability = 85f;
                stats.lightness = 0.55f;
                stats.weight = 1.75f;
                stats.groundDodgeDistance = 1.5f;
                stats.airDodgeDistance = 1.0f;
            }
            else if (ContainsAny(text, "flying", "bird", "wind", "浮", "飛", "鳥", "風"))
            {
                stats.groundMoveSpeed = 5.4f;
                stats.airMoveSpeed = 8.0f;
                stats.jumpForce = 17.5f;
                stats.airJumpHeightMultiplier = 0.60f;
                stats.walkSpeedRatio = 0.35f;
                stats.guardDurability = 60f;
                stats.lightness = 1.5f;
                stats.weight = 0.7f;
                stats.groundDodgeDistance = 2.4f;
                stats.airDodgeDistance = 3.0f;
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

        static SkillData BuildRangedSkill(string name, Element element, SkillSlot slot)
        {
            var skill = SampleSkillLibrary.RangedFireball();
            skill.slot = slot;
            skill.skill_name = $"{ElementLabel(element)} Shot";
            skill.description = $"{name} fires a ranged attack shaped by the prompt.";
            skill.element = element;
            return skill;
        }

        static SkillData BuildDashSkill(string name, Element element)
        {
            var skill = SampleSkillLibrary.SpecialDashStun();
            skill.skill_name = $"{ElementLabel(element)} Step";
            skill.description = $"{name} bursts forward and disrupts the opponent.";
            skill.element = element;
            return skill;
        }

        static SkillData BuildTrapSkill(string name, Element element)
        {
            var skill = SampleSkillLibrary.AreaTrap();
            skill.skill_name = $"{ElementLabel(element)} Field";
            skill.description = $"{name} leaves an effect that controls space.";
            skill.element = element;
            return skill;
        }

        static SkillData BuildAreaSkill(string name, Element element)
        {
            var skill = SampleSkillLibrary.AreaTrap();
            skill.skill_name = $"{ElementLabel(element)} Arc";
            skill.description = $"{name} sweeps a wide area nearby.";
            skill.element = element;
            skill.actions[0].type = "area_hitbox";
            skill.actions[0].follow_owner = true;
            skill.actions[0].spawn_x = 1.0f;
            skill.actions[0].spawn_y = 0.65f;
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
                chargeable = src.chargeable,
                max_charge_time = src.max_charge_time,
                follow_up_window = src.follow_up_window,
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
                clone.actions.Add(CloneAction(src.actions[i]));

            if (src.follow_up_actions != null)
            {
                clone.follow_up_actions = new List<SkillAction>();
                for (int i = 0; i < src.follow_up_actions.Count; i++)
                    clone.follow_up_actions.Add(CloneAction(src.follow_up_actions[i]));
            }

            return clone;
        }

        static SkillAction CloneAction(SkillAction a)
        {
            if (a == null) return null;
            return new SkillAction
            {
                type                = a.type,
                time                = a.time,
                duration            = a.duration,
                range               = a.range,
                spawn_x             = a.spawn_x,
                spawn_y             = a.spawn_y,
                size_y              = a.size_y,
                size_x              = a.size_x,
                hit_count           = a.hit_count,
                damage_override     = a.damage_override,
                follow_owner        = a.follow_owner,
                knockback_x         = a.knockback_x,
                knockback_y         = a.knockback_y,
                knockback_direction = a.knockback_direction,
                shape               = a.shape,
                hide_effect         = a.hide_effect,
                power               = a.power,
                direction           = a.direction,
                projectile_speed    = a.projectile_speed,
                projectile_lifetime = a.projectile_lifetime,
                projectile_angle    = a.projectile_angle,
                homing              = a.homing,
                homing_strength     = a.homing_strength,
                boomerang           = a.boomerang,
                projectile_count    = a.projectile_count,
                spread_angle        = a.spread_angle,
                gravity_scale       = a.gravity_scale,
                status              = a.status,
                chance              = a.chance,
            };
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
