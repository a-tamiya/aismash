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
        static readonly float[] MaxRange      = { 3.8f, 24f, 4.0f, 5.5f };
        static readonly float[] MinKnockback  = { 2.2f, 2.4f, 2.8f, 6f };  // 技ハメ防止のため最低限離す
        static readonly float[] MaxGuardDamage = { 2.0f, 2.6f, 2.8f, 5.0f };

        const float MaxStunTime  = 1.5f;
        const float MaxKnockback = 18f;
        const float BeamStartupSeconds = 0.3f;

        static readonly HashSet<string> SpatialOrigins = new HashSet<string>
        {
            "owner", "enemy", "midpoint", "stage_center", "left_edge", "right_edge",
        };
        static readonly HashSet<string> SpatialAnchors = new HashSet<string>
        {
            "auto", "body", "feet", "head", "weapon_tip",
        };
        static readonly HashSet<string> AimModes = new HashSet<string>
        {
            "facing", "enemy", "predicted_enemy", "away_enemy", "stage_center", "vector", "radial_out", "radial_in",
        };
        static readonly HashSet<string> SpatialPatterns = new HashSet<string>
        {
            "single", "fan", "parallel", "radial", "inward", "inward_ring", "mirrored", "line",
            "grid", "rain", "spiral", "pincer",
        };
        static readonly HashSet<string> HitboxShapes = new HashSet<string>
        {
            "box", "cone", "ring", "annulus", "arc", "line", "cross", "column",
        };
        static readonly HashSet<string> ActionConditions = new HashSet<string>
        {
            "grounded", "airborne", "enemy_close", "enemy_far", "low_hp", "high_hp", "enemy_above", "enemy_below",
        };

        public static void Apply(SkillData skill)
        {
            if (skill == null) return;
            var p  = skill.parameters;
            int si = (int)skill.slot;
            EnsureUsableActions(skill);
            EnforceUnarmedHideEffect(skill);
            bool bodyTech = IsBodyTechnique(skill);

            // ヒット数（上限を超えないよう）
            p.hit_count = Mathf.Clamp(p.hit_count, 1, 10);

            bool hasProjectile = HasAction(skill, "projectile") || HasAction(skill, "beam");
            int maxHitCount = Mathf.Clamp(MaxHitCount(skill), 1, 10);
            p.hit_count = Mathf.Clamp(Mathf.Max(p.hit_count, maxHitCount), 1, 10);

            // ダメージ上限（多段ヒットは1ヒットあたりに按分）
            float totalMaxDmg = MaxDamage[si];
            // 体術はリーチで武器持ちに劣るぶん、威力上限を高めて接近の見返りを作る
            if (bodyTech) totalMaxDmg *= 1.15f;
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

            // ── 体術（画像エフェクトに頼らない近接技）の底上げ ──
            // 素手・体当たり系はリーチが短いぶん、持続を長く・後隙を軽くして
            // 「発生が速く手数で押せる」格ゲーのインファイターらしい操作感にする。
            if (bodyTech)
            {
                p.active_time = Mathf.Max(p.active_time, 0.18f);
                if (skill.slot != SkillSlot.SmashSide)
                    p.recovery = Mathf.Min(p.recovery, 0.40f);
                if (p.range > 0f && p.range < 1.3f) p.range = 1.3f;
                foreach (var a in skill.actions)
                    if (a != null && a.range > 0f && a.range < 1.3f &&
                        (a.type == "melee_hitbox" || a.type == "body_hitbox" ||
                         a.type == "multi_hit" || a.type == "lifesteal"))
                        a.range = 1.3f;
            }

            if (HasAction(skill, "beam"))
                p.startup = Mathf.Max(p.startup, BeamStartupSeconds);

            // 設置・召喚技: キャラのロックアウト時間はactive_timeを短く打ち切る
            // 実寿命は action.duration が担う
            if (HasAction(skill, "trap_hitbox") || HasAction(skill, "summon") || HasAction(skill, "wall") ||
                HasAction(skill, "hazard_field") || HasAction(skill, "force_field") ||
                HasAction(skill, "healing_field"))
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
                    if (a == null) continue;
                    a.hit_count = Mathf.Clamp(a.hit_count, 0, 10);
                    if (a.hit_count > 1 && p.hit_count < a.hit_count)
                        p.hit_count = Mathf.Clamp(a.hit_count, 1, 10);
                    if (a.damage_override >= 0f)
                    {
                        float maxOverride = totalMaxDmg / Mathf.Max(1, a.hit_count);
                        a.damage_override = Mathf.Clamp(a.damage_override, 0f, maxOverride);
                    }

                    NormalizeSpatialAction(a);

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
                            "stun"        => Mathf.Clamp(a.duration > 0f ? a.duration : 0.4f, 0.4f, 0.7f),
                            "guard_break" => Mathf.Min(a.duration, 1.5f),
                            "burn"        => Mathf.Min(a.duration, 5f),
                            "slow"        => Mathf.Min(a.duration, 5f),
                            _             => a.duration,
                        };
                        a.chance = Mathf.Clamp01(a.chance);
                    }

                    // stunのstatus_duration（durationとは別フィールド）も必ず0.4〜0.7秒に収める
                    if (a.status == "stun" && a.status_duration > 0f)
                        a.status_duration = Mathf.Clamp(a.status_duration, 0.4f, 0.7f);

                    if (a.type == "melee_hitbox" || a.type == "body_hitbox" ||
                        a.type == "area_hitbox" || a.type == "trap_hitbox")
                    {
                        a.range = Mathf.Clamp(a.range, 0f, 5.5f);
                        if (a.size_x > 0f) a.size_x = Mathf.Clamp(a.size_x, 0.45f, 6.0f);
                        if (a.size_y > 0f) a.size_y = Mathf.Clamp(a.size_y, 0.35f, 4.0f);
                    }

                    if (a.type == "projectile")
                    {
                        a.projectile_speed    = Mathf.Clamp(a.projectile_speed, 0f, 18f);
                        a.projectile_lifetime = Mathf.Clamp(a.projectile_lifetime, 0f, 2.8f);
                        if (a.size_x > 0f) a.size_x = Mathf.Clamp(a.size_x, 0.7f, 3.0f);
                        if (a.size_y > 0f) a.size_y = Mathf.Clamp(a.size_y, 0.6f, 2.4f);
                        // 新フィールド
                        if (a.homing_strength != 0f) a.homing_strength = Mathf.Clamp01(a.homing_strength);
                        if (a.spread_angle > 0f)     a.spread_angle     = Mathf.Clamp(a.spread_angle, 5f, 60f);
                        if (a.projectile_count > 10) a.projectile_count = 10;
                        a.gravity_scale = Mathf.Clamp(a.gravity_scale, 0f, 3f);
                        // 拡張バリエーションのクランプ
                        a.explosion_radius = Mathf.Clamp(a.explosion_radius, 0f, 2.6f);
                        a.bounce_count     = Mathf.Clamp(a.bounce_count, 0, 4);
                        a.wave_amplitude   = Mathf.Clamp(a.wave_amplitude, 0f, 1.2f);
                        // 分裂弾: 子弾数と広がり角
                        if (a.split_count != 0) a.split_count = Mathf.Clamp(a.split_count, 2, 4);
                        if (a.split_angle > 0f) a.split_angle = Mathf.Clamp(a.split_angle, 10f, 80f);
                        // 衛星弾: 周回半径（range流用）を制限。爆発・分裂とは併用しない
                        if (a.orbit)
                        {
                            if (a.range > 0f) a.range = Mathf.Clamp(a.range, 0.8f, 3f);
                            a.explosion_radius = 0f;
                            a.split_count      = 0;
                        }
                        // 多発時は1発あたりダメージを按分
                        if (a.projectile_count > 1)
                        {
                            float perShotMax = Mathf.Max(1f, totalMaxDmg / a.projectile_count);
                            if (a.damage_override >= 0f)
                                a.damage_override = Mathf.Min(a.damage_override, perShotMax);
                            else
                                p.damage = Mathf.Min(p.damage, perShotMax);
                        }
                        if (a.projectile_count >= 6 || a.pattern_count >= 6)
                        {
                            p.recovery = Mathf.Max(p.recovery, 0.38f);
                            a.homing_strength = Mathf.Min(a.homing_strength, 0.65f);
                        }
                    }

                    if (a.type == "beam")
                    {
                        a.time = Mathf.Max(a.time, BeamStartupSeconds);
                        float maxBeamLength = skill.slot == SkillSlot.SmashSide ? 16f : 14f;
                        if (a.size_x > 0f) a.size_x = Mathf.Clamp(a.size_x, 2f, maxBeamLength);
                        if (a.size_y > 0f) a.size_y = Mathf.Clamp(a.size_y, 0.25f, 1.7f);
                        a.range    = a.range > 0f ? Mathf.Clamp(a.range, 2f, maxBeamLength) : 0f;
                        a.duration = Mathf.Clamp(a.duration, 0f, 0.12f);
                        if (a.size_x > 10f || a.range > 10f)
                        {
                            a.telegraph_time = Mathf.Max(a.telegraph_time, 0.75f);
                            p.recovery = Mathf.Max(p.recovery, 0.45f);
                        }
                    }

                    // dashのpowerに上限
                    if ((a.type == "dash" || a.type == "jump_attack" ||
                         a.type == "push_enemy" || a.type == "pull_enemy" ||
                         a.type == "teleport" || a.type == "uppercut" ||
                         a.type == "dive_attack") && a.power > 15f)
                        a.power = 15f;

                    // uppercut / dive_attack: 移動を伴う攻撃。判定サイズをクランプ
                    if (a.type == "uppercut" || a.type == "dive_attack")
                    {
                        if (a.size_x > 0f) a.size_x = Mathf.Clamp(a.size_x, 0.8f, 2.4f);
                        if (a.size_y > 0f) a.size_y = Mathf.Clamp(a.size_y, 1.0f, 3.0f);
                        if (a.duration > 0f) a.duration = Mathf.Clamp(a.duration, 0.2f, 0.6f);
                    }

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
                        if (!string.IsNullOrEmpty(a.status))
                            a.status_duration = a.status == "stun"
                                ? Mathf.Clamp(a.status_duration > 0f ? a.status_duration : 0.4f, 0.4f, 0.7f)
                                : Mathf.Clamp(a.status_duration > 0f ? a.status_duration : 1.5f, 0.1f, 5f);
                        if (a.power > 0f) a.power = Mathf.Clamp(a.power, 0.5f, 10f);
                        if (a.damage_override >= 0f)
                            a.damage_override = Mathf.Clamp(a.damage_override, 0f, totalMaxDmg * 0.6f);
                        // 召喚対象の体格を技ごとに表現する。極小使い魔〜大型獣まで許容しつつ、
                        // 画面を不当に塞ぐほどの巨大化は防ぐ。
                        if (a.size_x > 0f) a.size_x = Mathf.Clamp(a.size_x, 0.55f, 4.0f);
                        if (a.size_y > 0f) a.size_y = Mathf.Clamp(a.size_y, 0.65f, 4.8f);
                        a.gravity_scale = Mathf.Clamp(a.gravity_scale, 0f, 3f);
                        a.wave_amplitude = Mathf.Clamp(a.wave_amplitude, 0f, 1.5f);
                        if (a.orbit)
                        {
                            a.range = Mathf.Clamp(a.range > 0f ? a.range : 1.8f, 0.8f, 4f);
                            a.homing = false;
                            a.gravity_scale = 0f;
                        }
                    }

                    // wall: 地面に固定する破壊可能な壁。powerは耐久、durationは寿命。
                    if (a.type == "wall")
                    {
                        a.duration = Mathf.Clamp(a.duration > 0f ? a.duration : 3f, 1.2f, 7f);
                        a.power = Mathf.Clamp(a.power > 0f ? a.power : 20f, 8f, 45f);
                        a.size_x = Mathf.Clamp(a.size_x > 0f ? a.size_x : 1.6f, 0.8f, 4.8f);
                        a.size_y = Mathf.Clamp(a.size_y > 0f ? a.size_y : 2.5f, 1.2f, 4.5f);
                        if (string.IsNullOrEmpty(a.spawn_anchor)) a.spawn_anchor = "feet";
                    }

                    // barrier: 1技完全無効。powerは旧JSON互換のため残すが、耐久値には使わない。
                    if (a.type == "barrier")
                    {
                        if (a.power > 0f)    a.power    = Mathf.Clamp(a.power, 3f, 30f);
                        if (a.duration > 0f) a.duration = Mathf.Clamp(a.duration, 0.5f, 6f);
                    }

                    // heal_self: 1回の回復量上限（HP直値）。未指定は実行側で最大HPの5%。
                    if (a.type == "heal_self" && a.power > 0f)
                        a.power = Mathf.Clamp(a.power, 0f, 18f);

                    // gravity_well: 引力・半径・持続の上限。
                    // 拘束が強すぎる不満があったため、引力の上限と拘束時間の上限を引き下げ、
                    // 拘束されている側が自力で抜け出しやすくしてある（Fighter.StartGravityWell側でも入力抵抗を追加）。
                    if (a.type == "gravity_well")
                    {
                        if (a.power > 0f)    a.power    = Mathf.Clamp(a.power, 4f, 25f);
                        if (a.range > 0f)    a.range    = Mathf.Clamp(a.range, 1f, 5f);
                        a.duration = Mathf.Clamp(a.duration > 0f ? a.duration : 1.0f, 0.3f, 1.5f);
                    }

                    // hazard_field: 長時間残る多段領域。総ヒット数と寿命を抑えて、
                    // 一度触れただけでHPを奪い続ける理不尽な領域にしない。
                    if (a.type == "hazard_field")
                    {
                        a.duration = Mathf.Clamp(a.duration > 0f ? a.duration : 2f, 0.6f, 4f);
                        a.range = Mathf.Clamp(a.range > 0f ? a.range : 2.4f, 0.8f, 5f);
                        a.size_x = Mathf.Clamp(a.size_x > 0f ? a.size_x : a.range, 0.8f, 6f);
                        a.size_y = Mathf.Clamp(a.size_y > 0f ? a.size_y : a.size_x, 0.5f, 5f);
                        a.hit_count = Mathf.Clamp(a.hit_count > 1 ? a.hit_count : 3, 2, 6);
                    }

                    // force_field: 押し流す/吸い込む/上昇気流などの非ダメージ領域。
                    if (a.type == "force_field")
                    {
                        a.duration = Mathf.Clamp(a.duration > 0f ? a.duration : 1.2f, 0.35f, 2.5f);
                        a.range = Mathf.Clamp(a.range > 0f ? a.range : 3f, 1f, 5f);
                        a.power = Mathf.Clamp(a.power > 0f ? a.power : 10f, 3f, 18f);
                    }

                    // healing_field: 中に留まった時だけ回復する領域。powerは全回復量。
                    if (a.type == "healing_field")
                    {
                        a.duration = Mathf.Clamp(a.duration > 0f ? a.duration : 2.5f, 1f, 4f);
                        a.range = Mathf.Clamp(a.range > 0f ? a.range : 2.2f, 1f, 4f);
                        a.power = Mathf.Clamp(a.power > 0f ? a.power : 12f, 4f, 18f);
                    }

                    // position_swap: 相手と位置を交換する攪乱技。届く距離と予兆を保証する。
                    if (a.type == "position_swap")
                    {
                        a.range = Mathf.Clamp(a.range > 0f ? a.range : 4f, 1.5f, 6f);
                        a.telegraph_time = Mathf.Max(a.telegraph_time, 0.35f);
                        p.recovery = Mathf.Max(p.recovery, 0.38f);
                    }

                    // command_throw: 掴み範囲・高さの上限（ワイヤー投げでも届きすぎ防止）。
                    if (a.type == "command_throw")
                    {
                        if (a.range  > 0f) a.range  = Mathf.Clamp(a.range, 1f, 4.5f);
                        if (a.size_y > 0f) a.size_y = Mathf.Clamp(a.size_y, 1f, 3f);
                    }

                    // shockwave: 波1枚あたりのサイズと左右オフセットの上限。
                    if (a.type == "shockwave")
                    {
                        if (a.size_x > 0f) a.size_x = Mathf.Clamp(a.size_x, 0.45f, 4.5f);
                        if (a.size_y > 0f) a.size_y = Mathf.Clamp(a.size_y, 0.35f, 2.0f);
                        if (a.range  > 0f) a.range  = Mathf.Clamp(a.range, 0.5f, 4.5f);
                    }

                    // lifesteal: 近接判定＋吸収割合。melee扱いのサイズ・割合をクランプ。
                    if (a.type == "lifesteal")
                    {
                        a.range = Mathf.Clamp(a.range, 0f, 4.2f);
                        if (a.size_x > 0f) a.size_x = Mathf.Clamp(a.size_x, 0.45f, 4.5f);
                        if (a.size_y > 0f) a.size_y = Mathf.Clamp(a.size_y, 0.35f, 3.0f);
                        a.lifesteal_ratio = Mathf.Clamp(a.lifesteal_ratio > 0f ? a.lifesteal_ratio : 0.3f, 0f, 0.5f);
                    }
                }
            }

            // follow_up_actionsも同様にクランプ
            if (skill.follow_up_actions != null)
            {
                foreach (var fa in skill.follow_up_actions)
                {
                    if (fa == null) continue;
                    NormalizeSpatialAction(fa);
                    // 派生入力は最大3段の既存連携で回数を管理するため、action内repeatとの
                    // 二重増殖を禁止する。
                    fa.repeat_count = 1;
                    fa.repeat_interval = 0f;
                    fa.hit_count = 1;
                    if (fa.damage_override >= 0f)
                        fa.damage_override = Mathf.Clamp(fa.damage_override, 0f, totalMaxDmg * 0.35f);
                }
            }

            // ── 一貫性の強制（プロンプト指示をコード側でも保証する）──
            EnforceExclusiveActions(skill);
            NormalizeComplementaryConditionThresholds(skill);
            EnsureConditionalCoverage(skill);
            EnsureSmashDirectAttack(skill);
            SyncStartupWithActions(skill, si);
            EnsureMultiHitActiveTime(skill);
            // 相手の位置に発生する技（spawn_at_enemy）は強力なので、連発できないよう後隙を最低保証
            if (skill.actions != null)
                foreach (var a in skill.actions)
                    if (a != null && a.spawn_at_enemy)
                    {
                        p.recovery = Mathf.Max(p.recovery, 0.35f);
                        break;
                    }
            // attack_a のチャージは横スマッシュ入力（はじき＋A）と競合するため無効化
            if (skill.slot == SkillSlot.AttackA && skill.chargeable)
            {
                skill.chargeable = false;
                skill.max_charge_time = 0f;
            }
        }

        // 空間指定はAI生成値をそのまま実行せず、対応トークンとゲーム内で読める範囲に正規化する。
        // とくに相手位置・ステージ遠隔起点の攻撃は必ず予告時間を持たせ、見て避けられるようにする。
        static void NormalizeSpatialAction(SkillAction a)
        {
            a.spawn_origin = NormalizeToken(a.spawn_origin, SpatialOrigins);
            a.spawn_anchor = NormalizeToken(a.spawn_anchor, SpatialAnchors);
            a.aim_mode     = NormalizeToken(a.aim_mode, AimModes);
            a.pattern      = NormalizeToken(a.pattern, SpatialPatterns);
            a.shape        = NormalizeToken(a.shape, HitboxShapes);
            a.condition    = NormalizeToken(a.condition, ActionConditions);

            bool repeatable = a.type == "projectile" || a.type == "beam" ||
                              a.type == "melee_hitbox" || a.type == "body_hitbox" ||
                              a.type == "area_hitbox" || a.type == "trap_hitbox" ||
                              a.type == "summon" || a.type == "hazard_field";
            a.repeat_count = repeatable ? Mathf.Clamp(a.repeat_count > 0 ? a.repeat_count : 1, 1, 5) : 1;
            a.repeat_interval = a.repeat_count > 1
                ? Mathf.Clamp(a.repeat_interval > 0f ? a.repeat_interval : 0.18f, 0.08f, 0.65f)
                : 0f;
            if (a.condition == "low_hp" || a.condition == "high_hp")
            {
                // 「HP30%」を30として出したJSONも、0.3の比率指定として解釈する。
                if (a.condition_value > 1f && a.condition_value <= 100f)
                    a.condition_value *= 0.01f;
                a.condition_value = a.condition_value > 0f
                    ? Mathf.Clamp(a.condition_value, 0.05f, 0.95f)
                    : 0f;
            }
            else
            {
                a.condition_value = Mathf.Clamp(a.condition_value, 0f, 12f);
            }

            a.vector_x = Mathf.Clamp(a.vector_x, -1f, 1f);
            a.vector_y = Mathf.Clamp(a.vector_y, -1f, 1f);
            if (a.aim_mode == "vector" && a.vector_x * a.vector_x + a.vector_y * a.vector_y < 0.01f)
                a.aim_mode = "facing";

            a.rotation_angle = Mathf.Repeat(a.rotation_angle + 180f, 360f) - 180f;
            int maxPatternCount = a.type == "projectile" ? 10 : a.type == "summon" ? 6 : 4;
            if (a.pattern_count != 0)
                a.pattern_count = Mathf.Clamp(a.pattern_count, 1, maxPatternCount);
            if (a.pattern_spacing > 0f)
                a.pattern_spacing = Mathf.Clamp(a.pattern_spacing, 0.2f, 3f);
            if (a.pattern_radius > 0f)
                a.pattern_radius = Mathf.Clamp(a.pattern_radius, 0.5f, 6f);
            a.burst_interval = Mathf.Clamp(a.burst_interval, 0f, 0.5f);
            a.telegraph_time = Mathf.Clamp(a.telegraph_time, 0f, 2f);

            float outerExtent = a.size_x > 0f ? a.size_x : a.range;
            float outerRadius = (a.shape == "annulus" || a.shape == "arc" || a.shape == "ring")
                ? outerExtent * 0.5f
                : outerExtent;
            float innerMax = outerRadius > 0f ? Mathf.Max(0f, outerRadius * 0.85f) : 5f;
            a.inner_radius = Mathf.Clamp(a.inner_radius, 0f, innerMax);
            if (a.shape == "ring")
                a.inner_radius = 0f;
            else if (a.shape == "annulus" && a.inner_radius <= 0f)
                a.inner_radius = Mathf.Max(0.25f, outerRadius * 0.45f);
            if (a.shape == "arc")
                a.arc_angle = Mathf.Clamp(a.arc_angle > 0f ? a.arc_angle : 90f, 15f, 330f);
            else if (a.arc_angle > 0f)
                a.arc_angle = Mathf.Clamp(a.arc_angle, 15f, 360f);

            bool remoteOrigin = a.spawn_at_enemy || a.spawn_origin == "enemy" ||
                                a.spawn_origin == "midpoint" || a.spawn_origin == "stage_center" ||
                                a.spawn_origin == "left_edge" || a.spawn_origin == "right_edge";
            if (remoteOrigin && IsTelegraphedAttack(a.type))
                a.telegraph_time = Mathf.Max(a.telegraph_time, 0.4f);
            else if (remoteOrigin && a.type == "force_field")
                a.telegraph_time = Mathf.Max(a.telegraph_time, 0.3f);
        }

        static bool IsTelegraphedAttack(string type)
        {
            return type == "melee_hitbox" || type == "body_hitbox" || type == "projectile" ||
                   type == "area_hitbox" || type == "trap_hitbox" || type == "beam" ||
                   type == "lifesteal" || type == "summon" || type == "wall" ||
                   type == "hazard_field";
        }

        static string NormalizeToken(string value, HashSet<string> allowed)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string token = value.Trim().ToLowerInvariant();
            return allowed.Contains(token) ? token : null;
        }

        // 条件付きactionだけで構成された技が特定状況で完全な空振り操作にならないよう、
        // 地上/空中・近/遠・低/高HPの相補ペアだけを完全分岐として認める。
        // 片側しか生成されなかった場合は最初のgameplay actionを無条件へ戻す。
        static void EnsureConditionalCoverage(SkillData skill)
        {
            if (skill?.actions == null) return;
            bool hasGameplay = false;
            bool unconditional = false;
            var conditions = new HashSet<string>();
            SkillAction firstGameplay = null;
            foreach (var a in skill.actions)
            {
                if (a == null || !IsGameplayActionType(a.type)) continue;
                hasGameplay = true;
                firstGameplay ??= a;
                if (string.IsNullOrEmpty(a.condition)) unconditional = true;
                else conditions.Add(a.condition);
            }
            if (!hasGameplay || unconditional) return;

            bool covered = (conditions.Contains("grounded") && conditions.Contains("airborne")) ||
                           (conditions.Contains("enemy_close") && conditions.Contains("enemy_far")) ||
                           (conditions.Contains("low_hp") && conditions.Contains("high_hp"));
            if (!covered && firstGameplay != null)
            {
                firstGameplay.condition = null;
                firstGameplay.condition_value = 0f;
            }
        }

        // 近距離/遠距離を完全分岐として使う場合、両側の閾値を必ず1本へ統一する。
        // AIが近距離2.5・遠距離4.0のように別値を返しても、その間で技が不発にならない。
        static void NormalizeComplementaryConditionThresholds(SkillData skill)
        {
            if (skill?.actions == null) return;
            bool hasClose = false;
            bool hasFar = false;
            float sum = 0f;
            int count = 0;
            foreach (var a in skill.actions)
            {
                if (a == null) continue;
                if (a.condition == "enemy_close") hasClose = true;
                else if (a.condition == "enemy_far") hasFar = true;
                else continue;

                if (a.condition_value > 0f)
                {
                    sum += a.condition_value;
                    count++;
                }
            }
            if (!hasClose || !hasFar) return;

            float threshold = count > 0
                ? Mathf.Clamp(sum / count, 1.25f, 8f)
                : SkillConstants.EnemyDistanceBranchThreshold;
            foreach (var a in skill.actions)
                if (a != null && (a.condition == "enemy_close" || a.condition == "enemy_far"))
                    a.condition_value = threshold;
        }

        static bool IsGameplayActionType(string type)
        {
            switch (type)
            {
                case "melee_hitbox": case "body_hitbox": case "projectile": case "area_hitbox":
                case "trap_hitbox": case "beam": case "jump_attack": case "summon": case "wall":
                case "counter": case "reflector": case "buff_self": case "barrier": case "heal_self":
                case "command_throw": case "shockwave": case "gravity_well": case "hazard_field":
                case "force_field": case "healing_field": case "position_swap": case "lifesteal":
                case "uppercut": case "dive_attack":
                    return true;
                default:
                    return false;
            }
        }

        // 直接攻撃としてゲーム内で判定が出るaction種。
        static readonly HashSet<string> DirectAttackTypes = new HashSet<string>
        {
            "melee_hitbox", "body_hitbox", "projectile", "area_hitbox", "trap_hitbox", "beam",
            "jump_attack", "multi_hit", "dash+melee_hitbox", "shockwave", "lifesteal", "command_throw",
            "uppercut", "dive_attack", "summon", "hazard_field",
        };

        // counter / reflector / command_throw は同じ発動条件内では他の攻撃判定と混在させない。
        // ただし「地上はcounter、空中はdive_attack」のような完全分岐では、別条件側の
        // actionを消さず、1つの技ボタンで状況別の挙動を維持する。
        static void EnforceExclusiveActions(SkillData skill)
        {
            foreach (string type in new[] { "counter", "reflector", "command_throw" })
            {
                var exclusiveActions = skill.actions.FindAll(a => a != null && a.type == type);
                if (exclusiveActions.Count == 0) continue;
                string t = type;
                if (exclusiveActions.Exists(a => string.IsNullOrEmpty(a.condition)))
                {
                    skill.actions.RemoveAll(a => a == null || a.type != t);
                    return;
                }

                foreach (var exclusive in exclusiveActions)
                {
                    string condition = exclusive.condition;
                    skill.actions.RemoveAll(a =>
                        a == null || (a.type != t && a.condition == condition));
                }
            }
        }

        // smash_side は必ず直接攻撃を持つ（counterやbuffだけのスマッシュを防ぐ）。
        static void EnsureSmashDirectAttack(SkillData skill)
        {
            if (skill.slot != SkillSlot.SmashSide) return;
            foreach (var a in skill.actions)
                if (a != null && DirectAttackTypes.Contains(a.type)) return;

            skill.actions.Add(new SkillAction
            {
                type = "melee_hitbox",
                time = skill.parameters.startup,
                range = 2.2f, spawn_x = 1.3f, spawn_y = 1.0f, size_y = 1.8f,
                hit_count = 1,
            });
        }

        // 見た目（構え時間=startup）と攻撃判定の発生タイミングを一致させる。
        // AI出力では parameters.startup と actions[].time がずれることが多く、
        // 「構えている最中に判定が出る」「発生表記より遅く出る」の原因になる。
        // 最初の攻撃判定がちょうど startup の瞬間に出るよう、攻撃系actionの時刻を平行移動する。
        static void SyncStartupWithActions(SkillData skill, int si)
        {
            var p = skill.parameters;
            float first = float.MaxValue;
            foreach (var a in skill.actions)
                if (a != null && DirectAttackTypes.Contains(a.type))
                    first = Mathf.Min(first, a.time);
            if (first == float.MaxValue) return; // 攻撃判定を持たない技（counter等）は対象外

            float minStart = HasAction(skill, "beam") ? BeamStartupSeconds : 0f;
            float target   = Mathf.Max(Mathf.Clamp(p.startup, 0f, MaxStartup[si]), minStart);

            float delta = target - first;
            if (Mathf.Abs(delta) >= 0.01f)
            {
                foreach (var a in skill.actions)
                    if (a != null && DirectAttackTypes.Contains(a.type))
                        a.time = Mathf.Max(0f, a.time + delta);
            }
            p.startup = target;
        }

        // 多段技は再ヒット間隔（寿命/ヒット数、最低0.04秒）の都合で、持続が短いと
        // 表記ヒット数まで当たり切らない。全段入る最低限の active_time を確保する。
        static void EnsureMultiHitActiveTime(SkillData skill)
        {
            if (HasAction(skill, "trap_hitbox") || HasAction(skill, "summon") || HasAction(skill, "wall")) return;
            bool contact = HasAction(skill, "melee_hitbox") || HasAction(skill, "body_hitbox")
                        || HasAction(skill, "area_hitbox") || HasAction(skill, "jump_attack")
                        || HasAction(skill, "lifesteal") || HasAction(skill, "multi_hit")
                        || HasAction(skill, "uppercut") || HasAction(skill, "dive_attack");
            if (!contact) return;

            var p = skill.parameters;
            int hits = Mathf.Max(p.hit_count, MaxHitCount(skill));
            if (hits <= 1) return;
            p.active_time = Mathf.Clamp(Mathf.Max(p.active_time, 0.08f * hits), 0.05f, 0.6f);
        }

        // 体術技: 画像エフェクトを使わない近接技（body_hitbox / hide_effect付きmelee系 /
        // uppercut / dive_attack）だけで攻撃が構成されている技。武器エフェクト持ちや飛び道具は対象外。
        static bool IsBodyTechnique(SkillData skill)
        {
            if (skill?.actions == null) return false;
            bool hasContact = false;
            foreach (var a in skill.actions)
            {
                if (a == null) continue;
                switch (a.type)
                {
                    case "projectile":
                    case "beam":
                    case "summon":
                    case "trap_hitbox":
                        return false;
                    case "body_hitbox":
                    case "uppercut":
                    case "dive_attack":
                        hasContact = true; break;
                    case "melee_hitbox":
                    case "multi_hit":
                    case "dash+melee_hitbox":
                    case "lifesteal":
                        if (!a.hide_effect) return false;
                        hasContact = true; break;
                }
            }
            return hasContact;
        }

        static readonly string[] UnarmedKeywords =
        {
            "拳", "パンチ", "殴", "蹴", "キック", "肘", "膝", "頭突", "チョップ", "張り手",
            "掌", "平手", "体当た", "タックル", "組み付", "締め", "ラリアット",
            "ストレート", "アッパー", "フック", "正拳", "手刀",
        };
        static readonly string[] WeaponKeywords =
        {
            "剣", "刀", "斧", "槍", "鎌", "鞭", "杖", "弓", "銃", "砲", "ハンマー",
            "ナイフ", "ブレード", "ソード", "ランス", "武器", "爪", "クロー", "尻尾", "テール",
        };

        // 体術キーワードを含む無属性・物理の近接技は、画像エフェクトを生成・表示しない
        // （「素手のパンチなのに謎のエフェクト画像が出る」対策）。
        // 属性まとい系（炎の拳など）は演出としてエフェクト画像を残す。
        static void EnforceUnarmedHideEffect(SkillData skill)
        {
            if (skill.element != Element.Physical && skill.element != Element.None) return;
            string text = (skill.skill_name ?? "") + (skill.description ?? "");
            foreach (var w in WeaponKeywords) if (text.Contains(w)) return;
            bool unarmed = false;
            foreach (var k in UnarmedKeywords) if (text.Contains(k)) { unarmed = true; break; }
            if (!unarmed) return;
            foreach (var a in skill.actions)
                if (a != null && (a.type == "melee_hitbox" || a.type == "multi_hit" ||
                                  a.type == "dash+melee_hitbox" || a.type == "lifesteal"))
                    a.hide_effect = true;
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
                    case "wall":
                    case "counter":
                    case "reflector":
                    case "buff_self":
                    case "barrier":
                    case "heal_self":
                    case "command_throw":
                    case "shockwave":
                    case "gravity_well":
                    case "hazard_field":
                    case "force_field":
                    case "healing_field":
                    case "position_swap":
                    case "lifesteal":
                    case "uppercut":
                    case "dive_attack":
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
