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

        // follow_up: ヒット後の受付時間内に追加入力で発動
        public List<SkillAction> follow_up_actions;
        public float             follow_up_window;   // 受付秒数（0→デフォルト0.5s）

        // charge: 長押しで powerMultiplier 1.0→1.8
        public bool  chargeable;
        public float max_charge_time;  // 最大チャージ秒（0→デフォルト1.5s）
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

        // projectile: 基本
        public float  projectile_speed;
        public float  projectile_lifetime;

        // projectile: 発射角度（度数。0=水平, 45=斜め上, -45=斜め下, 90=真上）
        public float  projectile_angle;

        // projectile: 追尾
        public bool   homing;
        public float  homing_strength; // 0〜1。大きいほど曲がりやすい

        // projectile: ブーメラン（寿命の半分で折り返す）
        public bool   boomerang;

        // projectile: 多方向発射
        public int    projectile_count;  // 0/1=単発、2以上=多発
        public float  spread_angle;      // 発射間の広がり角（度数）。省略時15

        // projectile: 重力（0=無重力デフォルト、1=通常重力）
        public float  gravity_scale;

        // ノックバック方向: "away"(default)/"up"/"spike"/"toward"/"diagonal_up"/"ground_bounce"
        public string knockback_direction;

        // area_hitbox: 形状 "box"(default) / "cone"（前方扇形近似） / "ring"（周囲円形）
        public string shape;

        // apply_status / buff_self
        public string status;     // "stun"/"burn"/"slow"/"guard_break"/"speed"/"jump"/"transparent"/"damage"/"reflect"
        public float  chance = 1f;
    }
}
