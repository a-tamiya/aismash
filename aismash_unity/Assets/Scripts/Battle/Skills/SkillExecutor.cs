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
        readonly float[] _cooldowns = new float[4];
        bool _isExecuting;

        public bool  IsExecuting               => _isExecuting;
        public float GetCooldown(SkillSlot s)  => _cooldowns[(int)s];
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
            ResetCooldowns();
            Debug.Log($"[SkillExecutor] キャラクター「{data.characterName}」の技をロードしました。");
        }

        // JSONから直接ロード（フォールバックつき）
        public void LoadFromJson(string json, string fallbackName = "???")
        {
            var data = SkillJsonParser.ParseOrFallback(json, fallbackName);
            LoadCharacter(data);
        }

        public void ResetCooldowns()
        {
            for (int i = 0; i < _cooldowns.Length; i++) _cooldowns[i] = 0f;
        }

        void Update()
        {
            for (int i = 0; i < _cooldowns.Length; i++)
                if (_cooldowns[i] > 0f) _cooldowns[i] -= Time.deltaTime;
        }

        public bool TryUseSkill(SkillSlot slot)
        {
            int i = (int)slot;
            if (skills[i] == null)                return false;
            if (_isExecuting)                     return false;
            if (_cooldowns[i] > 0f)               return false;
            if (!_fighter.CanAct)                 return false;

            BattleLogger.Instance?.LogSkillUse(_fighter.PlayerIndex, slot, skills[i].skill_name);
            StartCoroutine(ExecuteSkill(skills[i]));
            return true;
        }

        IEnumerator ExecuteSkill(SkillData skill)
        {
            _isExecuting = true;
            _cooldowns[(int)skill.slot] = skill.parameters.cooldown;
            float totalDuration = skill.parameters.startup + skill.parameters.active_time + skill.parameters.recovery;
            _fighter.BeginSkillRecovery(totalDuration);

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
                    ExecuteAction(skill, a);
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

        void ExecuteAction(SkillData skill, SkillAction a)
        {
            switch (a.type)
            {
                case "melee_hitbox":   SpawnMeleeHitbox(skill, a); break;
                case "projectile":     SpawnProjectile(skill, a);  break;
                case "dash":           DoDash(a);                  break;
                case "apply_status":   ApplyOpponentStatus(a);     break;
                case "delay":          /* no-op: time制御で表現 */ break;
                default:
                    Debug.LogWarning($"[Skill] Unknown action type: {a.type}");
                    break;
            }
        }

        void SpawnMeleeHitbox(SkillData skill, SkillAction a)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            float range   = a.range > 0f ? a.range : skill.parameters.range;
            if (range <= 0f) range = 1.2f;

            Vector2 offset = new Vector2(dirSign * (range * 0.5f + 0.3f), 0f);
            Vector2 size   = new Vector2(range, 1.2f);
            float lifetime = skill.parameters.active_time > 0f ? skill.parameters.active_time : 0.12f;

            var hb = Hitbox.Spawn(_fighter, (Vector2)_fighter.transform.position + offset, size, lifetime);
            hb.transform.SetParent(_fighter.transform, worldPositionStays: true);
            float dmg = a.damage_override >= 0f ? a.damage_override : skill.parameters.damage;
            hb.Damage         = dmg;
            hb.Knockback      = skill.parameters.knockback;
            hb.KnockbackDir   = new Vector2(1f, 0.3f);
            hb.StunTime       = skill.parameters.stun_time;
            hb.GuardDamage    = skill.parameters.guard_damage;
            hb.Element        = skill.element;
            hb.MaxHits        = a.hit_count > 0 ? a.hit_count : skill.parameters.hit_count;
        }

        void SpawnProjectile(SkillData skill, SkillAction a)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            Vector2 spawn = (Vector2)_fighter.transform.position + new Vector2(dirSign * 0.6f, 0.2f);

            float speed    = a.projectile_speed    > 0f ? a.projectile_speed    : 9f;
            float lifetime = a.projectile_lifetime > 0f ? a.projectile_lifetime : 1.5f;

            var p = Projectile.Spawn(_fighter, spawn, new Vector2(dirSign, 0f), speed, lifetime);
            float dmg = a.damage_override >= 0f ? a.damage_override : skill.parameters.damage;
            p.Damage         = dmg;
            p.Knockback      = skill.parameters.knockback;
            p.StunTime       = skill.parameters.stun_time;
            p.GuardDamage    = skill.parameters.guard_damage;
            p.Element        = skill.element;
        }

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
