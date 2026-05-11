using UnityEngine;

namespace PromptFighters.Battle.Skills
{
    // 要件8.4: AIが出力した技パラメータに上限・下限を適用する。
    // プレイヤーは強いキャラを自由に作れるが、ゲームバランスは必ずここで保証する。
    public static class BalanceCorrector
    {
        // 技枠ごとの上限
        static readonly float[] MaxDamage     = { 14f, 12f, 10f, 30f }; // Close/Ranged/Special/Ultimate
        static readonly float[] MinCooldown   = {  1f,  2f,  4f,  8f };
        static readonly float[] MaxCooldown   = {2.5f,  4f,  7f, 15f };

        const float MaxRange     = 16f;  // ステージ幅超え防止
        const float MaxStunTime  =  1.5f;
        const float MaxKnockback = 15f;

        public static void Apply(SkillData skill)
        {
            if (skill == null) return;
            var p  = skill.parameters;
            int si = (int)skill.slot;

            // ダメージ上限（多段ヒットは1ヒットあたりに按分）
            float totalMaxDmg = MaxDamage[si];
            if (p.hit_count > 1)
            {
                float perHitMax = totalMaxDmg / p.hit_count;
                p.damage = Mathf.Clamp(p.damage, 0f, perHitMax);
            }
            else
            {
                p.damage = Mathf.Clamp(p.damage, 0f, totalMaxDmg);
            }

            // クールダウン
            p.cooldown = Mathf.Clamp(p.cooldown, MinCooldown[si], MaxCooldown[si]);

            // 射程
            p.range = Mathf.Clamp(p.range, 0.5f, MaxRange);

            // 怯み時間
            p.stun_time = Mathf.Clamp(p.stun_time, 0f, MaxStunTime);

            // ノックバック
            p.knockback = Mathf.Clamp(p.knockback, 0f, MaxKnockback);

            // ヒット数（上限を超えないよう）
            p.hit_count = Mathf.Clamp(p.hit_count, 1, 10);

            // startupは0以上
            p.startup     = Mathf.Max(0f, p.startup);
            p.active_time = Mathf.Max(0.05f, p.active_time);
            p.recovery    = Mathf.Max(0.05f, p.recovery);

            // actions内のdamage_overrideにも適用
            if (skill.actions != null)
            {
                foreach (var a in skill.actions)
                {
                    if (a.damage_override >= 0f)
                        a.damage_override = Mathf.Clamp(a.damage_override, 0f, totalMaxDmg);

                    // apply_statusのduration上限
                    if (a.type == "apply_status" && a.duration > 0f)
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

                    // dashのpowerに上限
                    if (a.type == "dash" && a.power > 15f) a.power = 15f;
                }
            }
        }
    }
}
