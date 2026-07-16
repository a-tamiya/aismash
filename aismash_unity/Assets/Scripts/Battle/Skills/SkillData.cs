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

        // ボス専用の追加技プール用。-1=既存4枠のslotポーズ/エフェクトを使う。
        // 0以上=CharacterData.extraPoseSprites/extraEffectSpritesの該当インデックスの専用画像を使う。
        public int extraSpriteIndex = -1;
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
        public bool   player_controlled;
        public float  knockback_x;
        public float  knockback_y;
        public bool   hide_effect;

        // 空間指定（未指定なら従来挙動）。spawn_origin を指定した新形式では
        // spawn_x / spawn_y を符号付きの明示オフセットとして扱う。
        public string spawn_origin;   // "owner" / "enemy" / "midpoint" / "stage_center" / "left_edge" / "right_edge"
        public string spawn_anchor;   // "auto" / "body" / "feet" / "head" / "weapon_tip"
        public string aim_mode;       // "facing" / "enemy" / "away_enemy" / "stage_center" / "vector" / "radial_out" / "radial_in"
        public float  vector_x;       // aim_mode="vector" の方向ベクトル
        public float  vector_y;
        public float  rotation_angle; // 判定・エフェクトの追加回転（度）

        // 同じ action を規則的に展開する配置パターン。
        public string pattern;        // "single" / "fan" / "parallel" / "radial" / "inward" / "mirrored" / "line"
        public int    pattern_count;   // 0=旧 projectile_count またはパターン既定値
        public float  pattern_spacing;
        public float  pattern_radius;
        public float  burst_interval; // 各要素の発生間隔（秒）
        public float  telegraph_time;  // 0=相手基準技の従来値0.4秒

        // shape の追加幾何パラメータ。annulus は inner_radius より内側が安全地帯、
        // arc は arc_angle の角度内だけ有効。cross では inner_radius を腕の太さに使う。
        public float  inner_radius;
        public float  arc_angle;

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

        // projectile: 追加バリエーション
        public float  explosion_radius;  // >0で着弾・壁・寿命切れに爆発（範囲ダメージ）
        public int    bounce_count;      // 地面・壁で跳ね返る回数（跳弾）
        public float  wave_amplitude;    // >0で上下にうねって飛ぶ（波状弾）
        public bool   pierce;            // trueで敵を貫通（1体につき1ヒット）

        // projectile: 分裂弾（壁ヒット・寿命切れで扇状に子弾へ分裂。花火・クラスター弾）
        public int    split_count;       // 2〜4で有効。子弾は威力半分・小型
        public float  split_angle;       // 子弾間の広がり角（度数）。省略時30

        // projectile: 衛星弾（自分の周囲を周回するビット・ファンネル。敵貫通・壁で消えない）
        public bool   orbit;             // trueで周回モード。rangeが周回半径、projectile_speedが周回速度

        // 発生位置: trueで相手の現在位置に発生する。
        // area_hitboxは0.4秒の警告表示つき、trap_hitboxは相手の足元、projectileは相手の頭上から落下。
        public bool   spawn_at_enemy;

        // ノックバック方向: 旧away/up/spike/toward/diagonal_up/ground_bounceに加え、
        // vector（符号付きknockback_x/y）/along_attack/from_origin/toward_origin。
        public string knockback_direction;

        // 判定形状: box / cone / ring / annulus / arc / line / cross / column
        public string shape;

        // apply_status / buff_self
        public string status;     // "stun"/"burn"/"slow"/"guard_break"/"speed"/"jump"/"transparent"/"damage"/"reflect"
        public float  status_duration;
        public float  chance = 1f;

        // lifesteal: 与ダメージのうち owner が回復する割合（0〜1）。melee/lifesteal で使用
        public float  lifesteal_ratio;
    }
}
