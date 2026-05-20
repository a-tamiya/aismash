using UnityEngine;
using System.Collections.Generic;

namespace PromptFighters.Battle.Skills
{
    // 要件8.4: AIが出力した技パラメータに上限・下限を適用する。
    // プレイヤーは強いキャラを自由に作れるが、ゲームバランスは必ずここで保証する。
    public static class BalanceCorrector
    {
        // 技枠ごとの上限
        static readonly float[] MaxDamage     = { 14f, 14f, 12f, 30f }; // attack_a/b/c/smash_side
        static readonly float[] MaxStartup    = { 0.12f, 0.18f, 0.22f, 0.32f };
        static readonly float[] MinRecovery   = { 0.10f, 0.16f, 0.24f, 0.18f };
        static readonly float[] MaxRecovery   = { 0.50f, 0.78f, 1.05f, 0.62f };
        static readonly float[] MaxRange      = { 3.4f, 22f, 3.6f, 4.2f };
        static readonly float[] MinKnockback  = { 2.2f, 2.4f, 2.8f, 6f };  // 技ハメ防止のため最低限離す
        static readonly float[] MaxGuardDamage = { 2.0f, 2.6f, 2.8f, 5.0f };

        const float MaxStunTime  = 1.5f;
        const float MaxKnockback = 18f;

        public static void Apply(SkillData skill)
        {
            if (skill == null) return;
            var p  = skill.parameters;
            int si = (int)skill.slot;
            EnsureUsableActions(skill);

            // ヒット数（上限を超えないよう）
            p.hit_count = Mathf.Clamp(p.hit_count, 1, 10);

            bool hasProjectile = HasAction(skill, "projectile") || HasAction(skill, "beam");
            int maxHitCount = Mathf.Clamp(MaxHitCount(skill), 1, 10);
            p.hit_count = Mathf.Clamp(Mathf.Max(p.hit_count, maxHitCount), 1, 10);

            // ダメージ上限（多段ヒットは1ヒットあたりに按分）
            float totalMaxDmg = MaxDamage[si];
            if (hasProjectile) totalMaxDmg = Mathf.Min(totalMaxDmg * 0.5f, si == 3 ? 10f : 6f);
            if (p.hit_count > 1) totalMaxDmg = Mathf.Min(totalMaxDmg, 6f);
            if (p.hit_count > 1)
            {
                float perHitMax = totalMaxDmg / p.hit_count;
                p.damage = Mathf.Clamp(p.damage, 0f, perHitMax);
            }
            else
            {
                p.damage = Mathf.Clamp(p.damage, 0f, totalMaxDmg);
            }

            // 後隙
            p.recovery = Mathf.Clamp(p.recovery, MinRecovery[si], MaxRecovery[si]);

            // 射程（近接はヒットボックスサイズ、飛び道具は射程距離）
            p.range = Mathf.Clamp(p.range, 0.5f, MaxRange[si]);

            // 怯み時間
            p.stun_time = Mathf.Clamp(p.stun_time, 0f, MaxStunTime);

            // ノックバック（必殺技は最低値保証）
            p.knockback = Mathf.Clamp(p.knockback * 1.25f, MinKnockback[si], MaxKnockback);
            p.guard_damage = Mathf.Clamp(p.guard_damage * 0.55f, 0f, MaxGuardDamage[si]);

            // startup: 0以上、スロットごとの上限を適用（極端に遅くならないよう）
            p.startup     = Mathf.Clamp(p.startup, 0f, MaxStartup[si]);
            p.active_time = Mathf.Max(0.05f, p.active_time);
            p.recovery    = Mathf.Max(MinRecovery[si], p.recovery);

            // 設置・召喚技: キャラのロックアウト時間はactive_timeを短く打ち切る
            // 実寿命は action.duration が担う
            if (HasAction(skill, "trap_hitbox") || HasAction(skill, "summon"))
                p.active_time = Mathf.Min(p.active_time, 0.10f);

            // リフレクター技: startup+active+recovery=1.0秒ちょうどに固定し、
            // 1秒後はリフレクト状態を保ったまま自由行動できるようにする
            if (HasAction(skill, "reflector"))
            {
                p.active_time = 0.10f;
                p.recovery    = Mathf.Max(MinRecovery[si], 1.0f - p.startup - 0.10f);
            }

            // スキルレベルのチャージ・フォローアップ制限
            if (skill.chargeable || skill.max_charge_time > 0f)
                skill.max_charge_time = Mathf.Clamp(skill.max_charge_time > 0f ? skill.max_charge_time : 1.5f, 1.0f, 3.0f);
            if (skill.follow_up_actions?.Count > 0)
                skill.follow_up_window = Mathf.Clamp(skill.follow_up_window > 0f ? skill.follow_up_window : 0.5f, 0.2f, 1.0f);

            // actions内のdamage_overrideにも適用
            if (skill.actions != null)
            {
                foreach (var a in skill.actions)
                {
                    a.hit_count = Mathf.Clamp(a.hit_count, 0, 10);
                    if (a.hit_count > 1 && p.hit_count < a.hit_count)
                        p.hit_count = Mathf.Clamp(a.hit_count, 1, 10);
                    if (a.damage_override >= 0f)
                    {
                        float maxOverride = totalMaxDmg / Mathf.Max(1, a.hit_count);
                        a.damage_override = Mathf.Clamp(a.damage_override, 0f, maxOverride);
                    }

                    if (a.type == "trap_hitbox")
                    {
                        if (a.duration <= 0f) a.duration = 2.5f; // 未指定は2.5秒
                        a.duration = Mathf.Clamp(a.duration, 0.5f, 5f);
                    }
                    // apply_statusのduration上限。trap_hitboxのdurationは設置寿命として扱う。
                    else if ((a.type == "apply_status" || a.type == "melee_hitbox" ||
                              a.type == "body_hitbox" ||
                              a.type == "area_hitbox" ||
                              a.type == "projectile") && a.duration > 0f)
                    {
                        a.duration = a.status switch
                        {
                            "stun"        => Mathf.Min(a.duration, MaxStunTime),
                            "guard_break" => Mathf.Min(a.duration, 1.5f),
                            "burn"        => Mathf.Min(a.duration, 5f),
                            "slow"        => Mathf.Min(a.duration, 5f),
                            _             => a.duration,
                        };
                        a.chance = Mathf.Clamp01(a.chance);
                    }

                    if (a.type == "melee_hitbox" || a.type == "body_hitbox" ||
                        a.type == "area_hitbox" || a.type == "trap_hitbox")
                    {
                        a.range = Mathf.Clamp(a.range, 0f, 4.2f);
                        if (a.size_x > 0f) a.size_x = Mathf.Clamp(a.size_x, 0.45f, 4.5f);
                        if (a.size_y > 0f) a.size_y = Mathf.Clamp(a.size_y, 0.35f, 3.0f);
                    }

                    if (a.type == "projectile")
                    {
                        a.projectile_speed    = Mathf.Clamp(a.projectile_speed, 0f, 18f);
                        a.projectile_lifetime = Mathf.Clamp(a.projectile_lifetime, 0f, 2.8f);
                        if (a.size_x > 0f) a.size_x = Mathf.Clamp(a.size_x, 0.45f, 2.4f);
                        if (a.size_y > 0f) a.size_y = Mathf.Clamp(a.size_y, 0.35f, 1.8f);
                        // 新フィールド
                        if (a.homing_strength != 0f) a.homing_strength = Mathf.Clamp01(a.homing_strength);
                        if (a.spread_angle > 0f)     a.spread_angle     = Mathf.Clamp(a.spread_angle, 5f, 60f);
                        if (a.projectile_count > 5)  a.projectile_count = 5;
                        a.gravity_scale = Mathf.Clamp(a.gravity_scale, 0f, 3f);
                        // 多発時は1発あたりダメージを按分
                        if (a.projectile_count > 1)
                        {
                            float perShotMax = Mathf.Max(1f, totalMaxDmg / a.projectile_count);
                            if (a.damage_override >= 0f)
                                a.damage_override = Mathf.Min(a.damage_override, perShotMax);
                            else
                                p.damage = Mathf.Min(p.damage, perShotMax);
                        }
                    }

                    if (a.type == "beam")
                    {
                        if (a.size_x > 0f) a.size_x = Mathf.Clamp(a.size_x, 2f, 12f);
                        if (a.size_y > 0f) a.size_y = Mathf.Clamp(a.size_y, 0.25f, 1.5f);
                        a.range    = a.range > 0f ? Mathf.Clamp(a.range, 2f, 12f) : 0f;
                        a.duration = Mathf.Clamp(a.duration, 0f, 0.12f);
                    }

                    // dashのpowerに上限
                    if ((a.type == "dash" || a.type == "jump_attack" ||
                         a.type == "push_enemy" || a.type == "pull_enemy" ||
                         a.type == "teleport") && a.power > 15f)
                        a.power = 15f;

                    if (a.type == "counter")
                    {
                        a.duration = Mathf.Clamp(a.duration > 0f ? a.duration : 1.0f, 1.0f, 1.5f);
                        if (a.damage_override >= 0f)
                            a.damage_override = Mathf.Clamp(a.damage_override, 0f, totalMaxDmg * 1.5f);
                    }

                    if (a.type == "reflector")
                        a.duration = Mathf.Clamp(a.duration > 0f ? a.duration : 1.0f, 1.0f, 3f);

                    if (a.type == "summon")
                    {
                        a.duration = Mathf.Clamp(a.duration > 0f ? a.duration : 3f, 1f, 6f);
                        if (a.power > 0f) a.power = Mathf.Clamp(a.power, 0.5f, 5f);
                        if (a.damage_override >= 0f)
                            a.damage_override = Mathf.Clamp(a.damage_override, 0f, totalMaxDmg * 0.6f);
                    }
                }
            }

            // follow_up_actionsも同様にクランプ
            if (skill.follow_up_actions != null)
            {
                foreach (var fa in skill.follow_up_actions)
                {
                    if (fa == null) continue;
                    fa.hit_count = 1;
                    if (fa.damage_override >= 0f)
                        fa.damage_override = Mathf.Clamp(fa.damage_override, 0f, totalMaxDmg * 0.35f);
                }
            }
        }

        static bool HasAction(SkillData skill, string type)
        {
            if (skill?.actions == null) return false;
            foreach (var a in skill.actions)
                if (a != null && a.type == type) return true;
            return false;
        }

        static void EnsureUsableActions(SkillData skill)
        {
            if (skill.actions == null)
                skill.actions = new List<SkillAction>();

            if (skill.actions.Count == 0 || !HasGameplayAction(skill))
            {
                skill.actions.Add(new SkillAction
                {
                    type = skill.slot == SkillSlot.SmashSide ? "area_hitbox" : "melee_hitbox",
                    time = 0f,
                    spawn_x = skill.slot == SkillSlot.SmashSide ? 1.35f : 0.9f,
                    spawn_y = skill.slot == SkillSlot.AttackC ? 0.35f : 0.75f,
                    size_x = skill.slot == SkillSlot.SmashSide ? 2.2f : 1.1f,
                    size_y = skill.slot == SkillSlot.SmashSide ? 1.4f : 0.9f,
                    hit_count = 1
                });
            }
        }

        static bool HasGameplayAction(SkillData skill)
        {
            if (skill?.actions == null) return false;
            foreach (var a in skill.actions)
            {
                if (a == null || string.IsNullOrEmpty(a.type)) continue;
                switch (a.type)
                {
                    case "melee_hitbox":
                    case "body_hitbox":
                    case "projectile":
                    case "area_hitbox":
                    case "trap_hitbox":
                    case "beam":
                    case "jump_attack":
                    case "summon":
                    case "counter":
                    case "reflector":
                    case "buff_self":
                        return true;
                }
            }
            return false;
        }

        static int MaxHitCount(SkillData skill)
        {
            int max = 1;
            if (skill?.actions == null) return max;
            foreach (var a in skill.actions)
                if (a != null) max = Mathf.Max(max, a.hit_count);
            return max;
        }
    }
}
