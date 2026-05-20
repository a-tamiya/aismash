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
            skill_name  = "ワンツーコンボ",
            description = "パンチから蹴りへ派生",
            element     = Element.Physical,
            risk_level  = RiskLevel.Low,
            parameters  = new SkillParameters
            {
                damage = 5, range = 1.2f, startup = 0.03f, active_time = 0.1f,
                recovery = 0.22f, hit_count = 1,
                knockback = 2.6f, stun_time = 0.12f, guard_damage = 1.0f,
            },
            actions = new List<SkillAction>
            {
                new SkillAction
                {
                    type = "melee_hitbox", time = 0f, range = 1.2f,
                    spawn_x = 0.8f, spawn_y = 0.75f, size_y = 1.0f, hit_count = 1,
                },
            },
            follow_up_actions = new List<SkillAction>
            {
                new SkillAction
                {
                    type = "melee_hitbox", time = 0f, range = 1.35f,
                    spawn_x = 0.9f, spawn_y = 0.25f, size_y = 0.95f, hit_count = 1,
                },
            },
            follow_up_window = 0.6f,
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
                damage = 10, range = 12f, startup = 0.08f, active_time = 0.1f,
                recovery = 0.34f, hit_count = 1,
                knockback = 5f, stun_time = 0.15f, guard_damage = 1.5f,
            },
            actions = new List<SkillAction>
            {
                new SkillAction
                {
                    type = "projectile", time = 0.08f,
                    projectile_speed = 10f, projectile_lifetime = 1.5f,
                    size_x = 1.15f, size_y = 0.74f,
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
                damage = 8, range = 1.5f, startup = 0.06f, active_time = 0.12f,
                recovery = 0.55f, hit_count = 1,
                knockback = 4f, stun_time = 0.4f, guard_damage = 1.2f,
            },
            actions = new List<SkillAction>
            {
                new SkillAction { type = "dash", time = 0f, power = 8f, direction = "forward" },
                new SkillAction
                {
                    type = "melee_hitbox", time = 0.06f, range = 1.5f, hit_count = 1,
                    follow_owner = true,
                    status = "stun", duration = 0.4f, chance = 1f,
                },
            },
        };

        public static SkillData AreaTrap() => new SkillData
        {
            slot        = SkillSlot.AttackC,
            skill_name  = "残響フィールド",
            description = "少し前に残る範囲攻撃",
            element     = Element.Wind,
            risk_level  = RiskLevel.Medium,
            parameters  = new SkillParameters
            {
                damage = 7, range = 2.2f, startup = 0.12f, active_time = 0.12f,
                recovery = 0.62f, hit_count = 1,
                knockback = 4f, stun_time = 0.12f, guard_damage = 1.5f,
            },
            actions = new List<SkillAction>
            {
                new SkillAction
                {
                    type = "trap_hitbox", time = 0.12f, duration = 0.45f,
                    spawn_x = 1.6f, spawn_y = 0.45f, size_x = 1.8f, size_y = 0.9f,
                    status = "slow", chance = 0.45f,
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
                damage = 24, range = 2f, startup = 0.15f, active_time = 0.15f,
                recovery = 0.46f, hit_count = 1,
                knockback = 10f, stun_time = 0.3f, guard_damage = 4.5f,
            },
            actions = new List<SkillAction>
            {
                new SkillAction { type = "dash", time = 0.05f, power = 4f, direction = "forward" },
                new SkillAction { type = "melee_hitbox", time = 0.15f, range = 2f, hit_count = 1 },
            },
        };

        public static SkillData ForSlot(SkillSlot slot) => slot switch
        {
            SkillSlot.AttackA   => CloseSlash(),
            SkillSlot.AttackB   => RangedFireball(),
            SkillSlot.AttackC   => AreaTrap(),
            SkillSlot.SmashSide => UltimateFinisher(),
            _                  => CloseSlash(),
        };

        public static void EquipDefaults(SkillExecutor executor)
        {
            executor.skills[(int)SkillSlot.AttackA]   = CloseSlash();
            executor.skills[(int)SkillSlot.AttackB]   = RangedFireball();
            executor.skills[(int)SkillSlot.AttackC]   = AreaTrap();
            executor.skills[(int)SkillSlot.SmashSide] = UltimateFinisher();
        }

        // CharacterData 版（SkillJsonParser のフォールバック用）
        public static void EquipDefaults(CharacterData data)
        {
            data.skills[(int)SkillSlot.AttackA]   = CloseSlash();
            data.skills[(int)SkillSlot.AttackB]   = RangedFireball();
            data.skills[(int)SkillSlot.AttackC]   = AreaTrap();
            data.skills[(int)SkillSlot.SmashSide] = UltimateFinisher();
        }
    }
}
