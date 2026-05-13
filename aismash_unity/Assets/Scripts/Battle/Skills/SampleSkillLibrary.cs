using System.Collections.Generic;
using UnityEngine;

namespace PromptFighters.Battle.Skills
{
    // Phase 2の動作確認・fallback用の固定技セット。
    // Phase 4以降のAI生成失敗時にも利用予定（要件15.2）。
    public static class SampleSkillLibrary
    {
        public static SkillData CloseSlash() => new SkillData
        {
            slot        = SkillSlot.AttackA,
            skill_name  = "連続斬り",
            description = "短射程の3段斬撃",
            element     = Element.Physical,
            risk_level  = RiskLevel.Low,
            parameters  = new SkillParameters
            {
                damage = 4, range = 1.3f, startup = 0.1f, active_time = 0.2f,
                recovery = 0.5f, hit_count = 3,
                knockback = 3f, stun_time = 0.15f, guard_damage = 2f,
            },
            actions = new List<SkillAction>
            {
                new SkillAction { type = "melee_hitbox", time = 0.1f, hit_count = 1 },
                new SkillAction { type = "melee_hitbox", time = 0.2f, hit_count = 1 },
                new SkillAction { type = "melee_hitbox", time = 0.3f, hit_count = 1 },
            },
        };

        public static SkillData RangedFireball() => new SkillData
        {
            slot        = SkillSlot.AttackB,
            skill_name  = "火球",
            description = "前方へ火球を撃つ",
            element     = Element.Fire,
            risk_level  = RiskLevel.Medium,
            parameters  = new SkillParameters
            {
                damage = 10, range = 12f, startup = 0.2f, active_time = 0.1f,
                recovery = 0.8f, hit_count = 1,
                knockback = 5f, stun_time = 0.2f, guard_damage = 3f,
            },
            actions = new List<SkillAction>
            {
                new SkillAction
                {
                    type = "projectile", time = 0.2f,
                    projectile_speed = 10f, projectile_lifetime = 1.5f,
                    status = "burn", duration = 2f, chance = 0.5f,
                },
            },
        };

        public static SkillData SpecialDashStun() => new SkillData
        {
            slot        = SkillSlot.AttackC,
            skill_name  = "雷撃ダッシュ",
            description = "前方ダッシュ→雷の一撃で短時間スタン",
            element     = Element.Lightning,
            risk_level  = RiskLevel.Medium,
            parameters  = new SkillParameters
            {
                damage = 8, range = 1.5f, startup = 0.15f, active_time = 0.15f,
                recovery = 1.5f, hit_count = 1,
                knockback = 4f, stun_time = 0.6f, guard_damage = 2f,
            },
            actions = new List<SkillAction>
            {
                new SkillAction { type = "dash", time = 0f, power = 8f, direction = "forward" },
                new SkillAction
                {
                    type = "melee_hitbox", time = 0.15f, range = 1.5f, hit_count = 1,
                    status = "stun", duration = 0.6f, chance = 1f,
                },
            },
        };

        public static SkillData UltimateFinisher() => new SkillData
        {
            slot        = SkillSlot.SmashSide,
            skill_name  = "渾身の一撃",
            description = "大きく踏み込んで強力な一撃",
            element     = Element.Physical,
            risk_level  = RiskLevel.High,
            parameters  = new SkillParameters
            {
                damage = 24, range = 2f, startup = 0.4f, active_time = 0.2f,
                recovery = 4.5f, hit_count = 1,
                knockback = 10f, stun_time = 0.4f, guard_damage = 8f,
            },
            actions = new List<SkillAction>
            {
                new SkillAction { type = "dash", time = 0.1f, power = 4f, direction = "forward" },
                new SkillAction { type = "melee_hitbox", time = 0.4f, range = 2f, hit_count = 1 },
            },
        };

        public static SkillData ForSlot(SkillSlot slot) => slot switch
        {
            SkillSlot.AttackA   => CloseSlash(),
            SkillSlot.AttackB   => RangedFireball(),
            SkillSlot.AttackC   => SpecialDashStun(),
            SkillSlot.SmashSide => UltimateFinisher(),
            _                  => CloseSlash(),
        };

        public static void EquipDefaults(SkillExecutor executor)
        {
            executor.skills[(int)SkillSlot.AttackA]   = CloseSlash();
            executor.skills[(int)SkillSlot.AttackB]   = RangedFireball();
            executor.skills[(int)SkillSlot.AttackC]   = SpecialDashStun();
            executor.skills[(int)SkillSlot.SmashSide] = UltimateFinisher();
        }

        // CharacterData 版（SkillJsonParser のフォールバック用）
        public static void EquipDefaults(CharacterData data)
        {
            data.skills[(int)SkillSlot.AttackA]   = CloseSlash();
            data.skills[(int)SkillSlot.AttackB]   = RangedFireball();
            data.skills[(int)SkillSlot.AttackC]   = SpecialDashStun();
            data.skills[(int)SkillSlot.SmashSide] = UltimateFinisher();
        }
    }
}
