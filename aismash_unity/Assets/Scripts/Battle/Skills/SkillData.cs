using System;
using System.Collections.Generic;

namespace PromptFighters.Battle.Skills
{
    // 要件12.3のJSON形式と一致させたデータ構造。
    // Phase 3 (JSONレシピ実行エンジン) でこのまま読み込めるようにフィールド名は snake_case 互換。

    [Serializable]
    public class SkillData
    {
        public SkillSlot   slot;
        public string      skill_name;
        public string      description;
        public Element     element;
        public RiskLevel   risk_level;
        public SkillParameters parameters = new SkillParameters();
        public List<SkillAction> actions  = new List<SkillAction>();
    }

    [Serializable]
    public class SkillParameters
    {
        public float damage;
        public float range;
        public float startup;
        public float active_time;
        public float recovery;
        public int   hit_count = 1;
        public float knockback;
        public float stun_time;
        public float guard_damage;
        public float move_force;
    }

    [Serializable]
    public class SkillAction
    {
        // 共通
        public string type;       // "melee_hitbox" / "body_hitbox" / "projectile" / "area_hitbox" / "trap_hitbox" / "dash" / "teleport" / "apply_status" / "buff_self" / "delay"
        public float  time;       // 技開始からの時間オフセット

        // 共通オプション
        public float  duration;
        public float  range;
        public float  spawn_x;
        public float  spawn_y;
        public float  size_x;
        public float  size_y;
        public int    hit_count = 1;
        public float  damage_override = -1f; // -1なら parameters.damage を使用
        public bool   follow_owner;
        public float  knockback_x;
        public float  knockback_y;
        public bool   hide_effect;

        // dash / buff_self
        public float  power;
        public string direction;  // "forward" / "backward"

        // projectile
        public float  projectile_speed;
        public float  projectile_lifetime;

        // apply_status
        public string status;     // "stun" / "burn" / "slow" / "guard_break"
        public float  chance = 1f;
    }
}
