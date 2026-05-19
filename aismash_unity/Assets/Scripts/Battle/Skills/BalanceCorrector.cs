using UnityEngine;

namespace PromptFighters.Battle.Skills
{
    // 要件8.4: AIが出力した技パラメータに上限・下限を適用する。
    // プレイヤーは強いキャラを自由に作れるが、ゲームバランスは必ずここで保証する。
    public static class BalanceCorrector
    {
        // 技枠ごとの上限
        static readonly float[] MaxDamage     = { 14f, 14f, 12f, 30f }; // attack_a/b/c/smash_side
        static readonly float[] MaxStartup    = { 0.12f, 0.18f, 0.22f, 0.32f };
        static readonly float[] MinRecovery   = { 0.14f, 0.20f, 0.30f, 0.20f };
        static readonly float[] MaxRecovery   = { 0.55f, 0.88f, 1.20f, 0.70f };
        static readonly float[] MaxRange      = { 3.4f, 22f, 3.6f, 4.2f };
        static readonly float[] MinKnockback  = { 0f,   0f,  0f,   4f };  // 横スマッシュは最低限吹き飛ぶ
        static readonly float[] MaxGuardDamage = { 2.0f, 2.6f, 2.8f, 5.0f };

        const float MaxStunTime  = 1.5f;
        const float MaxKnockback = 15f;

        public static void Apply(SkillData skill)
        {
            if (skill == null) return;
            var p  = skill.parameters;
            int si = (int)skill.slot;

            // ヒット数（上限を超えないよう）
            p.hit_count = Mathf.Clamp(p.hit_count, 1, 10);

            bool hasProjectile = HasAction(skill, "projectile");
            bool hasMultiHitAction = HasMultiHitAction(skill);

            // ダメージ上限（多段ヒットは1ヒットあたりに按分）
            float totalMaxDmg = MaxDamage[si];
            if (hasProjectile) totalMaxDmg *= 0.72f;
            if (hasMultiHitAction) totalMaxDmg *= 0.9f;
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
            p.recovery = Mathf.Clamp(p.recovery + 0.06f, MinRecovery[si], MaxRecovery[si]);

            // 射程（近接はヒットボックスサイズ、飛び道具は射程距離）
            p.range = Mathf.Clamp(p.range, 0.5f, MaxRange[si]);

            // 怯み時間
            p.stun_time = Mathf.Clamp(p.stun_time, 0f, MaxStunTime);

            // ノックバック（必殺技は最低値保証）
            p.knockback = Mathf.Clamp(p.knockback, MinKnockback[si], MaxKnockback);
            p.guard_damage = Mathf.Clamp(p.guard_damage * 0.55f, 0f, MaxGuardDamage[si]);

            // startup: 0以上、スロットごとの上限を適用（極端に遅くならないよう）
            p.startup     = Mathf.Clamp(p.startup, 0f, MaxStartup[si]);
            p.active_time = Mathf.Max(0.05f, p.active_time);
            p.recovery    = Mathf.Max(MinRecovery[si], p.recovery);

            // actions内のdamage_overrideにも適用
            if (skill.actions != null)
            {
                foreach (var a in skill.actions)
                {
                    if (a.hit_count > 1 && p.hit_count < a.hit_count)
                        p.hit_count = Mathf.Clamp(a.hit_count, 1, 10);
                    if (a.damage_override >= 0f)
                    {
                        float maxOverride = totalMaxDmg / Mathf.Max(1, a.hit_count);
                        a.damage_override = Mathf.Clamp(a.damage_override, 0f, maxOverride);
                    }

                    // apply_statusのduration上限
                    if ((a.type == "apply_status" || a.type == "melee_hitbox" ||
                         a.type == "body_hitbox" ||
                         a.type == "area_hitbox" || a.type == "trap_hitbox" ||
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
                        a.projectile_speed = Mathf.Clamp(a.projectile_speed, 0f, 18f);
                        a.projectile_lifetime = Mathf.Clamp(a.projectile_lifetime, 0f, 2.8f);
                        if (a.size_x > 0f) a.size_x = Mathf.Clamp(a.size_x, 0.45f, 2.4f);
                        if (a.size_y > 0f) a.size_y = Mathf.Clamp(a.size_y, 0.35f, 1.8f);
                    }

                    // dashのpowerに上限
                    if ((a.type == "dash" || a.type == "jump_attack" ||
                         a.type == "push_enemy" || a.type == "pull_enemy" ||
                         a.type == "teleport") && a.power > 15f)
                        a.power = 15f;
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

        static bool HasMultiHitAction(SkillData skill)
        {
            if (skill?.actions == null) return false;
            foreach (var a in skill.actions)
                if (a != null && a.hit_count > 1) return true;
            return false;
        }
    }
}
