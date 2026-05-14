using System.Collections;
using UnityEngine;
using PromptFighters.Battle.Skills.Json;

namespace PromptFighters.Battle.Skills
{
    // 1つのFighterに付与し、4枠の技を実行する。
    [RequireComponent(typeof(Fighter))]
    public class SkillExecutor : MonoBehaviour
    {
        public SkillData[] skills = new SkillData[4]; // index = SkillSlot
        public bool autoEquipSampleSkills = true;

        Fighter _fighter;
        bool _isExecuting;

        public bool  IsExecuting               => _isExecuting;
        public SkillData GetSkill(SkillSlot s) => skills[(int)s];

        void Awake()
        {
            _fighter = GetComponent<Fighter>();
            if (autoEquipSampleSkills && IsEmpty()) SampleSkillLibrary.EquipDefaults(this);
        }

        bool IsEmpty()
        {
            for (int i = 0; i < skills.Length; i++)
                if (skills[i] != null) return false;
            return true;
        }

        // CharacterData を受け取って技一式を差し替える（Phase 4のAI連携で呼ぶ）
        public void LoadCharacter(CharacterData data)
        {
            if (data == null) return;
            for (int i = 0; i < skills.Length; i++)
                skills[i] = data.skills[i];
            ResetSkillState();
            Debug.Log($"[SkillExecutor] キャラクター「{data.characterName}」の技をロードしました。");
        }

        // JSONから直接ロード（フォールバックつき）
        public void LoadFromJson(string json, string fallbackName = "???")
        {
            var data = SkillJsonParser.ParseOrFallback(json, fallbackName);
            LoadCharacter(data);
        }

        public void ResetSkillState()
        {
            _isExecuting = false;
            StopAllCoroutines();
        }

        public bool TryUseSkill(SkillSlot slot)
        {
            int i = (int)slot;
            if (skills[i] == null)                return false;
            if (_isExecuting)                     return false;
            if (!_fighter.CanAct)                 return false;
            if (slot == SkillSlot.SmashSide && !_fighter.IsGrounded) return false;

            BattleLogger.Instance?.LogSkillUse(_fighter.PlayerIndex, slot, skills[i].skill_name);
            StartCoroutine(ExecuteSkill(skills[i], 1f));
            return true;
        }

        public bool TryUseSkill(SkillSlot slot, float powerMultiplier)
        {
            int i = (int)slot;
            if (skills[i] == null) return false;
            if (_isExecuting) return false;
            if (!_fighter.CanAct) return false;
            if (slot == SkillSlot.SmashSide && !_fighter.IsGrounded) return false;

            float multiplier = Mathf.Clamp(powerMultiplier, 1f, 2f);
            BattleLogger.Instance?.LogSkillUse(_fighter.PlayerIndex, slot, skills[i].skill_name);
            StartCoroutine(ExecuteSkill(skills[i], multiplier));
            return true;
        }

        IEnumerator ExecuteSkill(SkillData skill, float powerMultiplier)
        {
            _isExecuting = true;
            float totalDuration = skill.parameters.startup + skill.parameters.active_time + skill.parameters.recovery;
            _fighter.BeginSkillRecovery(totalDuration);
            _fighter.ShowSkillSprite(skill.slot, totalDuration);

            // スキル発動フラッシュ
            var sr = _fighter.VisualRenderer;
            if (sr != null)
            {
                Color ec = SkillEnumParser.ElementColor(skill.element);
                sr.color = new Color(ec.r, ec.g, ec.b, 1f);
            }

            float t0 = Time.time;

            // アクションを time 昇順で順次実行（簡易: アクションは startup考慮済の time にスポーン）
            float elapsed = 0f;
            int actionIdx = 0;
            var actions = skill.actions;

            while (actionIdx < actions.Count)
            {
                elapsed = Time.time - t0;
                var a = actions[actionIdx];
                if (elapsed >= a.time)
                {
                    ExecuteAction(skill, a, powerMultiplier);
                    actionIdx++;
                }
                else
                {
                    yield return null;
                }
            }

            // recovery（後隙）が終わるまで待機
            while (Time.time - t0 < totalDuration) yield return null;

            _isExecuting = false;
        }

        void ExecuteAction(SkillData skill, SkillAction a, float powerMultiplier)
        {
            switch (a.type)
            {
                case "melee_hitbox":   SpawnMeleeHitbox(skill, a, powerMultiplier); break;
                case "projectile":     SpawnProjectile(skill, a, powerMultiplier);  break;
                case "dash":           DoDash(a);                  break;
                case "apply_status":   ApplyOpponentStatus(a);     break;
                case "delay":          /* no-op: time制御で表現 */ break;
                default:
                    Debug.LogWarning($"[Skill] Unknown action type: {a.type}");
                    break;
            }
        }

        void SpawnMeleeHitbox(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            float range   = a.range > 0f ? a.range : skill.parameters.range;
            if (range <= 0f) range = 1.2f;

            Vector2 baseOffset = DefaultMeleeOffset(skill.slot, range);
            float offsetX = a.spawn_x > 0f ? a.spawn_x : baseOffset.x;
            float offsetY = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : baseOffset.y;
            float height = a.size_y > 0f ? a.size_y : DefaultHitboxHeight(skill.slot);
            Vector2 offset = new Vector2(dirSign * offsetX, offsetY);
            Vector2 size   = new Vector2(range, height);
            float lifetime = skill.parameters.active_time > 0f ? skill.parameters.active_time : 0.12f;

            var hb = Hitbox.Spawn(_fighter, (Vector2)_fighter.transform.position + offset, size, lifetime);
            hb.transform.SetParent(_fighter.transform, worldPositionStays: true);
            float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage) * powerMultiplier;
            hb.Damage         = dmg;
            hb.Knockback      = skill.parameters.knockback * powerMultiplier;
            hb.KnockbackDir   = new Vector2(1f, 0.3f);
            hb.StunTime       = skill.parameters.stun_time;
            hb.GuardDamage    = skill.parameters.guard_damage;
            hb.Element        = skill.element;
            hb.EffectSprite   = _fighter.GetEffectSprite(skill.slot);
            hb.MaxHits        = a.hit_count > 0 ? a.hit_count : skill.parameters.hit_count;
        }

        void SpawnProjectile(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            Vector2 baseOffset = DefaultProjectileOffset(skill.slot);
            float offsetX = a.spawn_x > 0f ? a.spawn_x : baseOffset.x;
            float offsetY = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : baseOffset.y;
            Vector2 spawn = (Vector2)_fighter.transform.position + new Vector2(dirSign * offsetX, offsetY);

            float speed    = a.projectile_speed    > 0f ? a.projectile_speed    : 9f;
            float lifetime = a.projectile_lifetime > 0f ? a.projectile_lifetime : 1.5f;

            var p = Projectile.Spawn(_fighter, spawn, new Vector2(dirSign, 0f), speed, lifetime);
            float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage) * powerMultiplier;
            p.Damage         = dmg;
            p.Knockback      = skill.parameters.knockback * powerMultiplier;
            p.StunTime       = skill.parameters.stun_time;
            p.GuardDamage    = skill.parameters.guard_damage;
            p.Element        = skill.element;
            p.EffectSprite   = _fighter.GetEffectSprite(skill.slot);
        }

        static Vector2 DefaultMeleeOffset(SkillSlot slot, float range)
        {
            float x = range * 0.5f + 0.35f;
            return slot switch
            {
                SkillSlot.AttackA => new Vector2(x, 0.35f),
                SkillSlot.AttackB => new Vector2(x, 0.75f),
                SkillSlot.AttackC => new Vector2(x + 0.15f, 0.15f),
                SkillSlot.SmashSide => new Vector2(x + 0.25f, 0.55f),
                _ => new Vector2(x, 0.35f),
            };
        }

        static float DefaultHitboxHeight(SkillSlot slot) => slot switch
        {
            SkillSlot.AttackB => 1.0f,
            SkillSlot.AttackC => 1.45f,
            SkillSlot.SmashSide => 1.7f,
            _ => 1.2f,
        };

        static Vector2 DefaultProjectileOffset(SkillSlot slot) => slot switch
        {
            SkillSlot.AttackA => new Vector2(0.7f, 0.55f),
            SkillSlot.AttackB => new Vector2(0.8f, 1.05f),
            SkillSlot.AttackC => new Vector2(0.75f, 0.75f),
            SkillSlot.SmashSide => new Vector2(0.9f, 1.0f),
            _ => new Vector2(0.8f, 1.0f),
        };

        void DoDash(SkillAction a)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            if (a.direction == "backward") dirSign = -dirSign;
            float power = a.power > 0f ? a.power : 5f;
            _fighter.ApplyImpulse(new Vector2(dirSign * power, 0f));
        }

        // apply_status は相手に状態異常を付与する。近距離内でchance判定あり。
        void ApplyOpponentStatus(SkillAction a)
        {
            if (_fighter.Opponent == null) return;
            var st = SkillEnumParser.ParseStatus(a.status);
            if (st == StatusType.None) return;
            if (UnityEngine.Random.value > a.chance) return;
            _fighter.Opponent.ApplyStatus(st, a.duration);
        }
    }
}
