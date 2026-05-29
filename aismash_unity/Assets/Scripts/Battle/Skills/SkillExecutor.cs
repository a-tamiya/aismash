using System;
using System.Collections;
using System.Collections.Generic;
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
        const float HitboxVisualScale = SkillConstants.HitboxVisualScale;
        const int MaxFollowUpCount = SkillConstants.MaxFollowUpCount;
        const float FollowUpDamageMultiplier = SkillConstants.FollowUpDamageMultiplier;

        Fighter _fighter;
        float _sizeScale = 1f;
        bool _isExecuting;
        bool _currentSkillHit;
        int _skillSerial;

        // follow_up
        bool _followUpReady;
        float _followUpTimer;
        SkillData _followUpSkill;
        SkillSlot _followUpSlot;
        int _followUpCount;

        public bool      IsExecuting               => _isExecuting;
        public bool      IsFollowUpReady           => _followUpReady && _followUpTimer > 0f;
        public SkillSlot FollowUpSlot              => _followUpSlot;
        public SkillData GetSkill(SkillSlot s) => skills[(int)s];

        void Awake()
        {
            _fighter = GetComponent<Fighter>();
            if (autoEquipSampleSkills && IsEmpty()) SampleSkillLibrary.EquipDefaults(this);
        }

        void Update()
        {
            if (_followUpTimer > 0f)
            {
                _followUpTimer -= Time.deltaTime;
                if (_followUpTimer <= 0f) { _followUpReady = false; _followUpSkill = null; }
            }
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
            int n = data.skills != null ? data.skills.Length : 0;
            for (int i = 0; i < skills.Length; i++)
                skills[i] = i < n ? data.skills[i] : null;
            _sizeScale = Mathf.Clamp(data.sizeScale > 0f ? data.sizeScale : 1f, 0.5f, 2f);
            ResetSkillState();
            Debug.Log($"[SkillExecutor] キャラクター「{data.characterName}」の技をロードしました。(sizeScale={_sizeScale:F2})");
        }

        // JSONから直接ロード（フォールバックつき）
        public void LoadFromJson(string json, string fallbackName = "???")
        {
            var data = SkillJsonParser.ParseOrFallback(json, fallbackName);
            LoadCharacter(data);
        }

        public void ResetSkillState()
        {
            _isExecuting   = false;
            _followUpReady = false;
            _followUpTimer = 0f;
            _followUpSkill = null;
            _followUpSlot  = SkillSlot.AttackA;
            _followUpCount = 0;
            UnsubscribeCurrentSkillHit();
            StopAllCoroutines();
        }

        public bool TryExecuteFollowUp()
        {
            if (!IsFollowUpReady) return false;
            var skill = _followUpSkill;
            int nextFollowUpCount = Mathf.Clamp(_followUpCount + 1, 1, MaxFollowUpCount);
            _followUpReady = false;
            _followUpTimer = 0f;
            _followUpSkill = null;
            ResetSkillState();
            BattleLogger.Instance?.LogSkillUse(_fighter.PlayerIndex, skill.slot, skill.skill_name + $"（派生{nextFollowUpCount}）");
            GameAudioManager.Instance?.PlaySkill(skill);
            StartCoroutine(ExecuteFollowUp(skill, nextFollowUpCount));
            return true;
        }

        IEnumerator ExecuteFollowUp(SkillData skill, int followUpCount)
        {
            _isExecuting = true;
            _currentSkillHit = false;
            if (_fighter.Opponent != null)
                _fighter.Opponent.OnDamageReceived += MarkCurrentSkillHit;

            var actions = skill.follow_up_actions;
            int total = actions?.Count ?? 0;
            float t0 = Time.time;
            int idx  = 0;
            float totalTime = 0.15f;
            if (actions != null)
                foreach (var a in actions)
                    if (a != null)
                        totalTime = Mathf.Max(totalTime, a.time + (a.duration > 0f ? a.duration : 0.12f));

            _fighter.BeginSkillRecovery(totalTime);
            bool pullForCombo = followUpCount < MaxFollowUpCount;

            while (idx < total)
            {
                var a = actions[idx];
                if (a == null) { idx++; continue; }
                if (Time.time - t0 >= a.time)
                {
                    ExecuteFollowUpAction(skill, a, pullForCombo);
                    idx++;
                }
                else yield return null;
            }

            if (skill.follow_up_actions?.Count > 0 && followUpCount < MaxFollowUpCount)
                OpenFollowUpWindow(skill, followUpCount);

            while (Time.time - t0 < totalTime) yield return null;
            _isExecuting = false;
            UnsubscribeCurrentSkillHit();
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

        public bool TryUseDebugSkill(SkillData skill, float powerMultiplier = 1f)
        {
            if (skill == null) return false;
            ResetSkillState();
            float multiplier = Mathf.Clamp(powerMultiplier, 1f, 2f);
            BattleLogger.Instance?.LogSkillUse(_fighter.PlayerIndex, skill.slot, skill.skill_name);
            GameAudioManager.Instance?.PlaySkill(skill);
            StartCoroutine(ExecuteSkill(skill, multiplier));
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
            float firstBeamTime = FirstActionTime(skill, "beam");
            if (firstBeamTime > 0f)
                totalDuration = Mathf.Max(totalDuration, firstBeamTime + skill.parameters.active_time + recovery);
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

            // SmashSide: dashより後に判定が出るよう補正（melee/body 両対応）
            if (skill.slot == SkillSlot.SmashSide && actions != null)
            {
                bool hasDash = false;
                float latestDash = 0f;
                foreach (var ac in actions)
                    if (ac?.type == "dash") { hasDash = true; latestDash = Mathf.Max(latestDash, ac.time); }
                if (hasDash)
                    foreach (var ac in actions)
                        if (ac?.type == "melee_hitbox" || ac?.type == "body_hitbox")
                            ac.time = Mathf.Max(ac.time, latestDash + SkillConstants.SmashHitAfterDashDelay);
            }

            while (actionIdx < actions.Count)
            {
                elapsed = Time.time - t0;
                if (firstBeamTime > 0f && elapsed < firstBeamTime)
                    ShowBeamTelegraph(skill, elapsed / firstBeamTime);
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

            if (skill.follow_up_actions?.Count > 0)
                OpenFollowUpWindow(skill, 0);

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
                              a.type == "area_hitbox" || a.type == "jump_attack" || a.type == "beam";
                if (!melee) continue;
                hasMelee = true;
                float duration = a.duration > 0f ? a.duration : Mathf.Max(skill.parameters.active_time, 0.08f);
                latest = Mathf.Max(latest, a.time + duration);
            }
            return hasMelee ? latest + SkillConstants.WhiffCheckGrace : 0f;
        }

        static float EffectiveRecovery(SkillData skill)
        {
            if (skill == null) return 0f;
            return skill.slot == SkillSlot.SmashSide
                ? Mathf.Clamp(skill.parameters.recovery, SkillConstants.SmashRecoveryMin, SkillConstants.SmashRecoveryMax)
                : skill.parameters.recovery;
        }

        // アクション種 -> ハンドラのディスパッチ表。新しいアクションを足す際は
        // ここに1エントリ追加すればよく、中央のswitchを編集する必要がない。
        Dictionary<string, Action<SkillData, SkillAction, float>> _actionHandlers;

        void BuildActionHandlers()
        {
            _actionHandlers = new Dictionary<string, Action<SkillData, SkillAction, float>>
            {
                ["melee_hitbox"]       = SpawnMeleeHitbox,
                ["body_hitbox"]        = SpawnBodyHitbox,
                ["area_hitbox"]        = SpawnAreaHitbox,
                ["trap_hitbox"]        = SpawnTrapHitbox,
                ["projectile"]         = SpawnProjectile,
                ["beam"]               = SpawnBeam,
                ["jump_attack"]        = DoJumpAttack,
                ["dash+melee_hitbox"]  = (skill, a, pm) => { DoDash(a); SpawnMeleeHitbox(skill, a, pm); },
                ["multi_hit"]          = SpawnMeleeHitbox,
                ["dash"]               = (skill, a, pm) => DoDash(a),
                ["teleport"]           = (skill, a, pm) => DoTeleport(a),
                ["push_enemy"]         = (skill, a, pm) => PushOrPullOpponent(a, push: true),
                ["pull_enemy"]         = (skill, a, pm) => PushOrPullOpponent(a, push: false),
                ["buff_self"]          = (skill, a, pm) => BuffSelf(a),
                ["reflector"]          = (skill, a, pm) => DoReflector(a),
                ["counter"]            = (skill, a, pm) => DoCounter(a),
                ["summon"]             = SpawnSummon,
                ["apply_status"]       = (skill, a, pm) => ApplyOpponentStatus(a),
                ["delay"]              = (skill, a, pm) => { /* no-op: time制御で表現 */ },
            };
        }

        void ExecuteAction(SkillData skill, SkillAction a, float powerMultiplier)
        {
            if (_actionHandlers == null) BuildActionHandlers();
            if (_actionHandlers.TryGetValue(a.type, out var handler))
                handler(skill, a, powerMultiplier);
            else
                Debug.LogWarning($"[Skill] Unknown action type: {a.type}");
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
            // エフェクトなし（キャラ本体判定）は視覚補助がないぶんやや広めに
            if (a.hide_effect) { range *= 1.2f; height *= 1.2f; }
            // キャラサイズに合わせてヒットボックスをスケール
            range   *= _sizeScale;
            height  *= _sizeScale;
            offsetX *= _sizeScale;
            offsetY *= _sizeScale;
            Vector2 offset = new Vector2(dirSign * offsetX, offsetY);
            Vector2 size   = new Vector2(range * HitboxVisualScale, height * HitboxVisualScale);
            float lifetime = skill.parameters.active_time > 0f ? skill.parameters.active_time : 0.12f;
            if (skill.slot == SkillSlot.SmashSide) lifetime = Mathf.Max(lifetime, 0.15f);

            var hb = Hitbox.Spawn(_fighter, (Vector2)_fighter.transform.position + offset, size, lifetime);
            hb.FollowOwner = a.follow_owner;
            hb.OwnerLocalOffset = new Vector2(offsetX, offsetY);
            float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage) *
                        powerMultiplier * _fighter.DamageMultiplier;
            hb.Damage         = dmg;
            hb.DamageIncludesOwnerBoost = true;
            hb.Knockback      = skill.parameters.knockback * powerMultiplier;
            var (kbDir1, kbFixed1) = ComputeKnockback(a, 1f, 0.3f);
            hb.KnockbackDir      = kbDir1;
            hb.FixedKnockbackDir = kbFixed1;
            hb.GroundBounce      = a.knockback_direction == "ground_bounce";
            hb.StunTime       = skill.parameters.stun_time;
            hb.GuardDamage    = skill.parameters.guard_damage;
            hb.Element        = skill.element;
            hb.EffectSprite   = a.hide_effect ? null : _fighter.GetEffectSprite(skill.slot);
            hb.HideVisual     = a.hide_effect;
            hb.FlipEffectX    = !_fighter.FacingRight;
            hb.MaxHits        = a.hit_count > 0 ? a.hit_count : skill.parameters.hit_count;
            hb.IsSmashHit     = skill.slot == SkillSlot.SmashSide && powerMultiplier >= SkillConstants.SmashPowerThreshold;
            ApplyActionStatus(hb, a);
        }

        void SpawnBodyHitbox(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            // spawn_x=0 はキャラ中心（前方オフセットなし）。>0 で前方に張り出す
            float width   = a.size_x > 0f ? a.size_x : (a.range > 0f ? a.range : 1.9f);
            float height  = a.size_y > 0f ? a.size_y : 2.3f; // デフォルトは全身
            float offsetX = a.spawn_x * _sizeScale;           // 0=体の中心、正値=前方
            float offsetY = (!Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.75f) * _sizeScale;
            width  *= _sizeScale;
            height *= _sizeScale;
            float lifetime = a.duration > 0f ? a.duration
                           : (skill.parameters.active_time > 0f ? skill.parameters.active_time : 0.28f);

            var hb = Hitbox.Spawn(_fighter,
                (Vector2)_fighter.transform.position + new Vector2(dirSign * offsetX, offsetY),
                new Vector2(width * HitboxVisualScale, height * HitboxVisualScale),
                lifetime);
            hb.FollowOwner       = true;
            hb.OwnerLocalOffset  = new Vector2(offsetX, offsetY);
            hb.HideVisual        = true;
            float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage)
                        * powerMultiplier * _fighter.DamageMultiplier;
            hb.Damage            = dmg;
            hb.DamageIncludesOwnerBoost = true;
            hb.Knockback         = skill.parameters.knockback * powerMultiplier;
            var (kbDir, kbFixed) = ComputeKnockback(a, 1f, 0.3f);
            hb.KnockbackDir      = kbDir;
            hb.FixedKnockbackDir = kbFixed;
            hb.GroundBounce      = a.knockback_direction == "ground_bounce";
            hb.StunTime          = skill.parameters.stun_time;
            hb.GuardDamage       = skill.parameters.guard_damage;
            hb.Element           = skill.element;
            hb.MaxHits           = a.hit_count > 0 ? a.hit_count : skill.parameters.hit_count;
            ApplyActionStatus(hb, a);
        }

        void SpawnAreaHitbox(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign  = _fighter.FacingRight ? 1f : -1f;
            float lifetime = a.duration > 0f ? a.duration : Mathf.Max(skill.parameters.active_time, 0.12f);
            string shape   = string.IsNullOrEmpty(a.shape) ? "box" : a.shape;

            if (shape == "ring")
            {
                float radius   = (a.size_x > 0f ? a.size_x : (a.range > 0f ? a.range : 2f)) * 0.5f * _sizeScale;
                float offsetY  = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y * _sizeScale : 0.75f * _sizeScale;
                var hb = Hitbox.SpawnCircle(_fighter,
                    (Vector2)_fighter.transform.position + new Vector2(0f, offsetY),
                    radius, lifetime);
                float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage) *
                            powerMultiplier * _fighter.DamageMultiplier;
                hb.Damage         = dmg;
                hb.DamageIncludesOwnerBoost = true;
                hb.Knockback      = skill.parameters.knockback * powerMultiplier;
                var (kbDirR, kbFixedR) = ComputeKnockback(a, 1f, 0.25f);
                hb.KnockbackDir      = kbDirR;
                hb.FixedKnockbackDir = kbFixedR;
                hb.GroundBounce      = a.knockback_direction == "ground_bounce";
                hb.StunTime      = skill.parameters.stun_time;
                hb.GuardDamage   = skill.parameters.guard_damage;
                hb.Element       = skill.element;
                hb.MaxHits       = a.hit_count > 0 ? a.hit_count : skill.parameters.hit_count;
                hb.FollowOwner   = a.follow_owner;
                hb.OwnerLocalOffset = new Vector2(0f, offsetY / _sizeScale);
                ApplyActionStatus(hb, a);
                return;
            }

            // cone: 前方向に広く縦に薄い扇形近似
            float width, height, offsetX, offsetY2;
            if (shape == "cone")
            {
                width   = a.size_x > 0f ? a.size_x : (a.range > 0f ? a.range * 1.4f : 3.0f);
                height  = a.size_y > 0f ? a.size_y : width * 0.45f;
                offsetX = !Mathf.Approximately(a.spawn_x, 0f) ? a.spawn_x : width * 0.52f;
                offsetY2= !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.55f;
            }
            else // box (default)
            {
                width   = a.size_x > 0f ? a.size_x : (a.range > 0f ? a.range : skill.parameters.range);
                if (width <= 0f) width = 2f;
                height  = a.size_y > 0f ? a.size_y : width;
                offsetX = !Mathf.Approximately(a.spawn_x, 0f) ? a.spawn_x : width * 0.2f;
                offsetY2= !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.6f;
            }
            width    *= _sizeScale;
            height   *= _sizeScale;
            offsetX  *= _sizeScale;
            offsetY2 *= _sizeScale;

            var hbox = SpawnConfiguredHitbox(
                skill, a, powerMultiplier,
                (Vector2)_fighter.transform.position + new Vector2(dirSign * offsetX, offsetY2),
                new Vector2(width, height),
                lifetime);
            hbox.FollowOwner      = a.follow_owner;
            hbox.OwnerLocalOffset = new Vector2(offsetX, offsetY2);
            if (shape == "cone")
                hbox.SetDebugColor(new Color(0.3f, 0.9f, 1f, 0.6f)); // 水色: コーン判定
        }

        void SpawnTrapHitbox(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            float width = a.size_x > 0f ? a.size_x : Mathf.Max(0.8f, a.range > 0f ? a.range : skill.parameters.range);
            float height = a.size_y > 0f ? a.size_y : 0.9f;
            float offsetX = !Mathf.Approximately(a.spawn_x, 0f) ? a.spawn_x : width * 0.8f;
            float offsetY = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.35f;
            width   *= _sizeScale;
            height  *= _sizeScale;
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
            float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage) *
                        powerMultiplier * _fighter.DamageMultiplier;
            hb.Damage         = dmg;
            hb.DamageIncludesOwnerBoost = true;
            hb.Knockback      = skill.parameters.knockback * powerMultiplier;
            var (kbDir2, kbFixed2) = ComputeKnockback(a, 1f, 0.25f);
            hb.KnockbackDir      = kbDir2;
            hb.FixedKnockbackDir = kbFixed2;
            hb.GroundBounce      = a.knockback_direction == "ground_bounce";
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
            float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage) *
                        powerMultiplier * _fighter.DamageMultiplier;
            Vector2 desiredSize = new Vector2(
                (a.size_x > 0f ? a.size_x : Mathf.Clamp(speed * lifetime * 0.08f, 0.74f, 1.74f)) * HitboxVisualScale * _sizeScale,
                (a.size_y > 0f ? a.size_y : 0.75f) * HitboxVisualScale * _sizeScale);
            var (kbDir, kbFixed) = ComputeKnockback(a, 1f, 0.3f);

            int count = a.projectile_count > 1 ? a.projectile_count : 1;
            float spreadDeg = a.spread_angle > 0f ? a.spread_angle : 15f;
            float totalSpread = count > 1 ? spreadDeg * (count - 1) : 0f;

            for (int i = 0; i < count; i++)
            {
                float angleDeg = a.projectile_angle + (count > 1 ? -totalSpread * 0.5f + spreadDeg * i : 0f);
                float rad = angleDeg * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(dirSign * Mathf.Cos(rad), Mathf.Sin(rad)).normalized;

                var p = Projectile.Spawn(_fighter, spawn, dir, speed, lifetime);
                p.Damage                   = dmg;
                p.DamageIncludesOwnerBoost = true;
                p.Knockback                = skill.parameters.knockback * powerMultiplier;
                p.KnockbackDir             = kbDir;
                p.FixedKnockbackDir        = kbFixed;
                p.GroundBounce             = a.knockback_direction == "ground_bounce";
                p.StunTime                 = skill.parameters.stun_time;
                p.GuardDamage              = skill.parameters.guard_damage;
                p.Status                   = SkillEnumParser.ParseStatus(a.status);
                p.StatusDuration           = a.duration;
                p.StatusChance             = Mathf.Clamp01(a.chance);
                p.Element                  = skill.element;
                p.EffectSprite             = a.hide_effect ? null : _fighter.GetEffectSprite(skill.slot);
                p.HideVisual               = a.hide_effect;
                p.FlipEffectX              = !_fighter.FacingRight;
                p.DesiredWorldSize         = desiredSize;
                p.GravityScale             = a.gravity_scale;
                p.IsBoomerang              = a.boomerang;
                if ((a.homing || a.homing_strength > 0f) && _fighter.Opponent != null)
                {
                    p.HomingTarget   = _fighter.Opponent.transform;
                    p.HomingStrength = a.homing_strength > 0f ? a.homing_strength : 0.5f;
                }
            }
        }

        void SpawnBeam(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            float width   = a.size_x > 0f ? a.size_x : (a.range > 0f ? a.range : 7f);
            float height  = a.size_y > 0f ? a.size_y : 0.5f;
            height = Mathf.Max(height, Mathf.Clamp(width * 0.1f, 0.55f, 1.2f));
            float offsetX = a.spawn_x > 0f ? a.spawn_x : width * 0.5f;
            float offsetY = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.8f;
            width   *= _sizeScale;
            height  *= _sizeScale;
            offsetX *= _sizeScale;
            offsetY *= _sizeScale;
            float lifetime = a.duration > 0f ? a.duration : 0.07f;

            var hb = SpawnConfiguredHitbox(
                skill, a, powerMultiplier,
                (Vector2)_fighter.transform.position + new Vector2(dirSign * offsetX, offsetY),
                new Vector2(width, height),
                lifetime);
            hb.MaxHits = a.hit_count > 1 ? a.hit_count : 5; // 貫通
        }

        float FirstActionTime(SkillData skill, string type)
        {
            float time = float.MaxValue;
            if (skill?.actions == null) return -1f;
            foreach (var a in skill.actions)
                if (a != null && a.type == type)
                    time = Mathf.Min(time, a.time);
            return time == float.MaxValue ? -1f : time;
        }

        void ShowBeamTelegraph(SkillData skill, float charge01)
        {
            _fighter.ShowSkillCharge(charge01);
            var sr = _fighter.VisualRenderer;
            if (sr == null) return;

            Color ec = SkillEnumParser.ElementColor(skill.element);
            float pulse = (Mathf.Sin(Time.time * 28f) + 1f) * 0.5f;
            Color warmup = Color.Lerp(Color.white, ec, 0.45f + 0.4f * pulse);
            sr.color = new Color(warmup.r, warmup.g, warmup.b, 1f);
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
            PromptFighters.Audio.GameAudioManager.Instance?.PlayTeleport();
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
            float range = a.range > 0f ? a.range : 5f;
            float height = a.size_y > 0f ? a.size_y : 3.5f;
            if (Mathf.Abs(delta.x) > range || Mathf.Abs(delta.y) > height) return;

            float dir = Mathf.Sign(delta.x);
            if (dir == 0f) dir = _fighter.FacingRight ? 1f : -1f;
            if (!push) dir = -dir;
            float power = Mathf.Clamp(a.power > 0f ? a.power : 5.5f, 1.5f, 10f);
            float up = Mathf.Abs(a.knockback_y) > 0.01f ? a.knockback_y : 0.75f;
            _fighter.Opponent.ApplyImpulse(new Vector2(dir * power, up), 0.24f);
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
                case "transparent":
                    _fighter.StartTemporaryInvincible(Mathf.Min(duration, 1.2f));
                    break;
                case "reflect":
                    _fighter.StartTemporaryReflect(Mathf.Min(duration, 3f));
                    break;
                default:
                    _fighter.StartTemporaryDamageBoost(Mathf.Clamp(multiplier, 1f, 1.6f), duration);
                    break;
            }
        }

        void DoReflector(SkillAction a)
        {
            float duration = a.duration > 0f ? Mathf.Min(a.duration, 3f) : 0.8f;
            _fighter.StartTemporaryReflect(duration);
        }

        void DoCounter(SkillAction a)
        {
            float duration  = a.duration > 0f ? Mathf.Clamp(a.duration, 0.1f, 1.5f) : 0.4f;
            float damage    = a.damage_override >= 0f ? a.damage_override : 10f;
            float kx        = !Mathf.Approximately(a.knockback_x, 0f) ? Mathf.Abs(a.knockback_x) : 1f;
            float ky        = !Mathf.Approximately(a.knockback_y, 0f) ? Mathf.Abs(a.knockback_y) : 0.4f;
            float forceMag  = Mathf.Clamp(damage * 0.9f, 6f, 18f);
            _fighter.StartCounter(duration, damage, forceMag, new Vector2(kx, ky).normalized, 0.3f);
        }

        void SpawnSummon(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign  = _fighter.FacingRight ? 1f : -1f;
            float spawnX   = a.spawn_x > 0f ? a.spawn_x : 1.5f;
            float spawnY   = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0f;
            Vector2 pos    = (Vector2)_fighter.transform.position + new Vector2(dirSign * spawnX * _sizeScale, spawnY * _sizeScale);
            float lifetime = a.duration > 0f ? a.duration : 3f;
            float speed    = a.power > 0f ? a.power : 2.5f;
            float dmg      = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage * 0.5f) * powerMultiplier;
            float kb       = skill.parameters.knockback * 0.6f * powerMultiplier;
            Vector2 desiredSize = new Vector2(
                (a.size_x > 0f ? a.size_x : 0.9f) * _sizeScale,
                (a.size_y > 0f ? a.size_y : 1.2f) * _sizeScale);
            SummonEntity.Spawn(_fighter, pos, speed, lifetime, dmg, kb, skill.element,
                a.hide_effect ? null : _fighter.GetEffectSprite(skill.slot), desiredSize, a);
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

        (Vector2 kbDir, bool isFixed) ComputeKnockback(SkillAction a, float defaultX, float defaultY)
        {
            float facingSign = _fighter != null && _fighter.FacingRight ? 1f : -1f;
            float x = !Mathf.Approximately(a.knockback_x, 0f) ? Mathf.Abs(a.knockback_x) : defaultX;
            float y = !Mathf.Approximately(a.knockback_y, 0f) ? Mathf.Abs(a.knockback_y) : defaultY;
            return (string.IsNullOrEmpty(a.knockback_direction) ? "away" : a.knockback_direction) switch
            {
                "up"           => (new Vector2(0f,                  1.5f), true),
                "spike"        => (new Vector2(facingSign * 0.15f, -1.2f), true),
                "toward"       => (new Vector2(-facingSign * x,       y  ), true),
                "diagonal_up"  => (new Vector2(facingSign * 0.4f,   1.2f), true),
                "ground_bounce"=> (new Vector2(facingSign * 0.25f, -1.4f), true),
                _              => (new Vector2(x, y),                      false),
            };
        }

        static void ApplyActionStatus(Hitbox hb, SkillAction a)
        {
            var st = SkillEnumParser.ParseStatus(a.status);
            if (st == StatusType.None) return;
            hb.Status = st;
            hb.StatusDuration = a.duration;
            hb.StatusChance = Mathf.Clamp01(a.chance);
        }

        void OpenFollowUpWindow(SkillData skill, int followUpCount)
        {
            float window = skill.follow_up_window > 0f ? skill.follow_up_window : 0.5f;
            _followUpReady = true;
            _followUpTimer = window;
            _followUpSkill = skill;
            _followUpSlot  = skill.slot;
            _followUpCount = Mathf.Clamp(followUpCount, 0, MaxFollowUpCount);
        }

        void ExecuteFollowUpAction(SkillData skill, SkillAction action, bool pullForCombo)
        {
            string savedDir = action.knockback_direction;
            float savedDamage = skill.parameters.damage;
            float savedKnockback = skill.parameters.knockback;
            float savedKnockbackX = action.knockback_x;
            float savedKnockbackY = action.knockback_y;
            float savedDamageOverride = action.damage_override;

            skill.parameters.damage = savedDamage * FollowUpDamageMultiplier;
            if (action.damage_override >= 0f)
                action.damage_override *= FollowUpDamageMultiplier;

            if (!pullForCombo)
            {
                ExecuteAction(skill, action, 1f);
                skill.parameters.damage = savedDamage;
                action.damage_override = savedDamageOverride;
                return;
            }

            action.knockback_direction = "toward";
            action.knockback_x = savedKnockbackX > 0f ? Mathf.Min(savedKnockbackX, 0.55f) : 0.55f;
            action.knockback_y = savedKnockbackY > 0f ? Mathf.Min(savedKnockbackY, 0.18f) : 0.18f;
            skill.parameters.knockback = Mathf.Min(savedKnockback, 1.8f);

            ExecuteAction(skill, action, 1f);

            action.knockback_direction = savedDir;
            action.knockback_x = savedKnockbackX;
            action.knockback_y = savedKnockbackY;
            action.damage_override = savedDamageOverride;
            skill.parameters.damage = savedDamage;
            skill.parameters.knockback = savedKnockback;
        }
    }
}
