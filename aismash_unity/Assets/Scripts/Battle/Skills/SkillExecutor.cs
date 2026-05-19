using System.Collections;
using UnityEngine;
using PromptFighters.Audio;
using PromptFighters.Battle.Skills.Json;

namespace PromptFighters.Battle.Skills
{
    // 1つのFighterに付与し、4枠の技を実行する。
    [RequireComponent(typeof(Fighter))]
    public class SkillExecutor : MonoBehaviour
    {
        public SkillData[] skills = new SkillData[4]; // index = SkillSlot
        public bool autoEquipSampleSkills = true;
        const float HitboxVisualScale = 0.9f;

        Fighter _fighter;
        bool _isExecuting;
        bool _currentSkillHit;
        int _skillSerial;

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
            UnsubscribeCurrentSkillHit();
            StopAllCoroutines();
        }

        public bool TryUseSkill(SkillSlot slot)
        {
            int i = (int)slot;
            if (skills[i] == null)                return false;
            if (_isExecuting)                     return false;
            if (!_fighter.CanAct)                 return false;

            BattleLogger.Instance?.LogSkillUse(_fighter.PlayerIndex, slot, skills[i].skill_name);
            GameAudioManager.Instance?.PlaySkill(skills[i]);
            StartCoroutine(ExecuteSkill(skills[i], 1f));
            return true;
        }

        public bool TryUseSkill(SkillSlot slot, float powerMultiplier)
        {
            int i = (int)slot;
            if (skills[i] == null) return false;
            if (_isExecuting) return false;
            if (!_fighter.CanAct) return false;

            float multiplier = Mathf.Clamp(powerMultiplier, 1f, 2f);
            BattleLogger.Instance?.LogSkillUse(_fighter.PlayerIndex, slot, skills[i].skill_name);
            GameAudioManager.Instance?.PlaySkill(skills[i]);
            StartCoroutine(ExecuteSkill(skills[i], multiplier));
            return true;
        }

        IEnumerator ExecuteSkill(SkillData skill, float powerMultiplier)
        {
            _isExecuting = true;
            int serial = ++_skillSerial;
            _currentSkillHit = false;
            if (_fighter.Opponent != null)
                _fighter.Opponent.OnDamageReceived += MarkCurrentSkillHit;
            float recovery = EffectiveRecovery(skill);
            float totalDuration = skill.parameters.startup + skill.parameters.active_time + recovery;
            _fighter.BeginSkillRecovery(totalDuration);
            _fighter.ShowSkillSprite(skill.slot, totalDuration);
            float whiffDelay = WhiffCheckDelay(skill);
            if (whiffDelay > 0f)
                StartCoroutine(PlayWhiffIfMissed(serial, whiffDelay));

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
            UnsubscribeCurrentSkillHit();
        }

        void MarkCurrentSkillHit(float damage, bool wasBlocked)
        {
            _currentSkillHit = true;
        }

        void UnsubscribeCurrentSkillHit()
        {
            if (_fighter != null && _fighter.Opponent != null)
                _fighter.Opponent.OnDamageReceived -= MarkCurrentSkillHit;
        }

        IEnumerator PlayWhiffIfMissed(int serial, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_isExecuting && serial == _skillSerial && !_currentSkillHit)
                GameAudioManager.Instance?.PlayMeleeWhiff();
        }

        static float WhiffCheckDelay(SkillData skill)
        {
            if (skill?.actions == null) return 0f;
            float latest = 0f;
            bool hasMelee = false;
            for (int i = 0; i < skill.actions.Count; i++)
            {
                var a = skill.actions[i];
                if (a == null) continue;
                bool melee = a.type == "melee_hitbox" || a.type == "body_hitbox" ||
                              a.type == "area_hitbox" || a.type == "jump_attack";
                if (!melee) continue;
                hasMelee = true;
                float duration = a.duration > 0f ? a.duration : Mathf.Max(skill.parameters.active_time, 0.08f);
                latest = Mathf.Max(latest, a.time + duration);
            }
            return hasMelee ? latest + 0.03f : 0f;
        }

        static float EffectiveRecovery(SkillData skill)
        {
            if (skill == null) return 0f;
            return skill.slot == SkillSlot.SmashSide
                ? Mathf.Clamp(skill.parameters.recovery, 0.20f, 0.70f)
                : skill.parameters.recovery;
        }

        void ExecuteAction(SkillData skill, SkillAction a, float powerMultiplier)
        {
            switch (a.type)
            {
                case "melee_hitbox":   SpawnMeleeHitbox(skill, a, powerMultiplier); break;
                case "body_hitbox":    SpawnBodyHitbox(skill, a, powerMultiplier);  break;
                case "area_hitbox":    SpawnAreaHitbox(skill, a, powerMultiplier);  break;
                case "trap_hitbox":    SpawnTrapHitbox(skill, a, powerMultiplier);  break;
                case "projectile":     SpawnProjectile(skill, a, powerMultiplier);  break;
                case "jump_attack":    DoJumpAttack(skill, a, powerMultiplier);     break;
                case "dash":           DoDash(a);                  break;
                case "teleport":       DoTeleport(a);              break;
                case "push_enemy":     PushOrPullOpponent(a, push: true);  break;
                case "pull_enemy":     PushOrPullOpponent(a, push: false); break;
                case "buff_self":      BuffSelf(a);                 break;
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
            Vector2 size   = new Vector2(range * HitboxVisualScale, height * HitboxVisualScale);
            float lifetime = skill.parameters.active_time > 0f ? skill.parameters.active_time : 0.12f;

            var hb = Hitbox.Spawn(_fighter, (Vector2)_fighter.transform.position + offset, size, lifetime);
            hb.FollowOwner = a.follow_owner;
            hb.OwnerLocalOffset = new Vector2(offsetX, offsetY);
            float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage) * powerMultiplier;
            hb.Damage         = dmg;
            hb.Knockback      = skill.parameters.knockback * powerMultiplier;
            hb.KnockbackDir   = KnockbackDir(a, 1f, 0.3f);
            hb.StunTime       = skill.parameters.stun_time;
            hb.GuardDamage    = skill.parameters.guard_damage;
            hb.Element        = skill.element;
            hb.EffectSprite   = a.hide_effect ? null : _fighter.GetEffectSprite(skill.slot);
            hb.HideVisual     = a.hide_effect;
            hb.FlipEffectX    = !_fighter.FacingRight;
            hb.MaxHits        = a.hit_count > 0 ? a.hit_count : skill.parameters.hit_count;
            ApplyActionStatus(hb, a);
        }

        void SpawnBodyHitbox(SkillData skill, SkillAction a, float powerMultiplier)
        {
            a.follow_owner = true;
            a.hide_effect = true;
            if (a.range <= 0f) a.range = 0.85f;
            if (a.size_y <= 0f) a.size_y = 1.45f;
            if (Mathf.Approximately(a.spawn_y, 0f)) a.spawn_y = 0.75f;
            SpawnMeleeHitbox(skill, a, powerMultiplier);
        }

        void SpawnAreaHitbox(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            float width = a.size_x > 0f ? a.size_x : (a.range > 0f ? a.range : skill.parameters.range);
            if (width <= 0f) width = 2f;
            float height = a.size_y > 0f ? a.size_y : width;
            float offsetX = !Mathf.Approximately(a.spawn_x, 0f) ? a.spawn_x : width * 0.2f;
            float offsetY = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.6f;
            float lifetime = a.duration > 0f ? a.duration : Mathf.Max(skill.parameters.active_time, 0.12f);

            var hb = SpawnConfiguredHitbox(
                skill, a, powerMultiplier,
                (Vector2)_fighter.transform.position + new Vector2(dirSign * offsetX, offsetY),
                new Vector2(width, height),
                lifetime);
            hb.FollowOwner = a.follow_owner;
            hb.OwnerLocalOffset = new Vector2(offsetX, offsetY);
        }

        void SpawnTrapHitbox(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            float width = a.size_x > 0f ? a.size_x : Mathf.Max(0.8f, a.range > 0f ? a.range : skill.parameters.range);
            float height = a.size_y > 0f ? a.size_y : 0.9f;
            float offsetX = !Mathf.Approximately(a.spawn_x, 0f) ? a.spawn_x : width * 0.8f;
            float offsetY = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.35f;
            float lifetime = a.duration > 0f ? a.duration : Mathf.Max(skill.parameters.active_time, 0.35f);

            SpawnConfiguredHitbox(
                skill, a, powerMultiplier,
                (Vector2)_fighter.transform.position + new Vector2(dirSign * offsetX, offsetY),
                new Vector2(width, height),
                lifetime);
        }

        Hitbox SpawnConfiguredHitbox(SkillData skill, SkillAction a, float powerMultiplier,
                                     Vector2 position, Vector2 size, float lifetime)
        {
            size *= HitboxVisualScale;
            var hb = Hitbox.Spawn(_fighter, position, size, lifetime);
            float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage) * powerMultiplier;
            hb.Damage         = dmg;
            hb.Knockback      = skill.parameters.knockback * powerMultiplier;
            hb.KnockbackDir   = KnockbackDir(a, 1f, 0.25f);
            hb.StunTime       = skill.parameters.stun_time;
            hb.GuardDamage    = skill.parameters.guard_damage;
            hb.Element        = skill.element;
            hb.EffectSprite   = a.hide_effect ? null : _fighter.GetEffectSprite(skill.slot);
            hb.HideVisual     = a.hide_effect;
            hb.FlipEffectX    = !_fighter.FacingRight;
            hb.MaxHits        = a.hit_count > 0 ? a.hit_count : skill.parameters.hit_count;
            ApplyActionStatus(hb, a);
            return hb;
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
            p.Status         = SkillEnumParser.ParseStatus(a.status);
            p.StatusDuration = a.duration;
            p.StatusChance   = Mathf.Clamp01(a.chance);
            p.Element        = skill.element;
            p.EffectSprite   = a.hide_effect ? null : _fighter.GetEffectSprite(skill.slot);
            p.HideVisual     = a.hide_effect;
            p.FlipEffectX    = !_fighter.FacingRight;
            p.DesiredWorldSize = new Vector2(
                (a.size_x > 0f ? a.size_x : Mathf.Clamp(speed * lifetime * 0.08f, 0.74f, 1.74f)) * HitboxVisualScale,
                (a.size_y > 0f ? a.size_y : 0.75f) * HitboxVisualScale);
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
            float up = a.knockback_y;
            _fighter.ApplyImpulse(new Vector2(dirSign * power, up));
        }

        void DoTeleport(SkillAction a)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            if (a.direction == "backward") dirSign = -dirSign;
            float distance = Mathf.Clamp(a.power > 0f ? a.power : 2.2f, 0.5f, 4f);
            Vector3 pos = _fighter.transform.position + new Vector3(dirSign * distance, 0f, 0f);
            var bm = PromptFighters.Battle.BattleManager.Instance;
            if (bm != null)
                pos.x = Mathf.Clamp(pos.x, bm.StageMinX + 0.5f, bm.StageMaxX - 0.5f);
            _fighter.transform.position = pos;
        }

        void DoJumpAttack(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float lift = a.power > 0f ? a.power : 5f;
            _fighter.ApplyImpulse(new Vector2(0f, lift));
            SpawnAreaHitbox(skill, a, powerMultiplier);
        }

        void PushOrPullOpponent(SkillAction a, bool push)
        {
            if (_fighter.Opponent == null) return;

            Vector2 delta = _fighter.Opponent.transform.position - _fighter.transform.position;
            float range = a.range > 0f ? a.range : 3f;
            float height = a.size_y > 0f ? a.size_y : 1.5f;
            if (Mathf.Abs(delta.x) > range || Mathf.Abs(delta.y) > height) return;

            float dir = Mathf.Sign(delta.x);
            if (dir == 0f) dir = _fighter.FacingRight ? 1f : -1f;
            if (!push) dir = -dir;
            float power = a.power > 0f ? a.power : 4f;
            _fighter.Opponent.ApplyImpulse(new Vector2(dir * power, a.knockback_y));
        }

        void BuffSelf(SkillAction a)
        {
            float duration = Mathf.Max(0.1f, a.duration);
            float multiplier = a.power > 0f ? a.power : 1.2f;
            switch (a.status)
            {
                case "speed":
                    _fighter.StartTemporarySpeedChange(Mathf.Clamp(multiplier, 1f, 1.7f), duration);
                    break;
                case "jump":
                    _fighter.StartTemporaryJumpChange(Mathf.Clamp(multiplier, 1f, 1.5f), duration);
                    break;
                case "invincible":
                    _fighter.StartTemporaryInvincible(Mathf.Min(duration, 1.2f));
                    break;
                default:
                    _fighter.StartTemporaryDamageBoost(Mathf.Clamp(multiplier, 1f, 1.6f), duration);
                    break;
            }
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

        static Vector2 KnockbackDir(SkillAction a, float defaultX, float defaultY)
        {
            float x = !Mathf.Approximately(a.knockback_x, 0f) ? Mathf.Abs(a.knockback_x) : defaultX;
            float y = !Mathf.Approximately(a.knockback_y, 0f) ? Mathf.Abs(a.knockback_y) : defaultY;
            return new Vector2(x, y);
        }

        static void ApplyActionStatus(Hitbox hb, SkillAction a)
        {
            var st = SkillEnumParser.ParseStatus(a.status);
            if (st == StatusType.None) return;
            hb.Status = st;
            hb.StatusDuration = a.duration;
            hb.StatusChance = Mathf.Clamp01(a.chance);
        }
    }
}
