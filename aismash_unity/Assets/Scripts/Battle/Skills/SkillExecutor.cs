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
        // ボス専用の追加技プール（4枠システムとは独立。通常キャラは常に空）。FighterAIがボスのときだけ使う。
        public List<SkillData> extraSkills = new List<SkillData>();
        SkillData _lastRandomSkill; // TryUseRandomSkillの直前の選択（連発防止用）
        public bool autoEquipSampleSkills = true;
        const float HitboxVisualScale = SkillConstants.HitboxVisualScale;
        const int MaxFollowUpCount = SkillConstants.MaxFollowUpCount;
        const float FollowUpDamageMultiplier = SkillConstants.FollowUpDamageMultiplier;
        static Material s_telegraphLineMaterial;

        Fighter _fighter;
        CharacterVoicePlayer _voicePlayer;
        // キャラ固有の基準サイズ（プリセット/ボス）。技生成時の実効サイズはギミックの巨大化/縮小化も乗算する。
        float _baseSizeScale = 1f;
        float _sizeScale => Mathf.Clamp(
            _baseSizeScale * (_fighter != null ? _fighter.PermSizeMult : 1f), 0.3f, 2.5f);
        bool _isExecuting;
        bool _currentSkillHit;
        int _skillSerial;
        int _impactShownSerial = -1;
        float _currentSkillEndTime;
        readonly List<GameObject> _activeTelegraphs = new List<GameObject>();
        // ヒット検知の購読先。協力モードではOpponentが毎フレーム変わるため、
        // 購読時の相手を保持して確実に解除する（リーク防止）。
        Fighter _skillHitSubscribedTo;

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

        // 技を発動した瞬間に発火（チュートリアルの操作検知用）。派生・デバッグ発動も含む。
        public event System.Action<SkillSlot> OnSkillExecuted;

        void Awake()
        {
            _fighter = GetComponent<Fighter>();
            _voicePlayer = GetComponent<CharacterVoicePlayer>();
            if (_voicePlayer == null) _voicePlayer = gameObject.AddComponent<CharacterVoicePlayer>();
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
            extraSkills = data.extraSkills != null ? new List<SkillData>(data.extraSkills) : new List<SkillData>();
            _baseSizeScale = Mathf.Clamp(data.sizeScale > 0f ? data.sizeScale : 1f, 0.5f, 2f);
            _voicePlayer?.Configure(data);
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
            _lastRandomSkill = null;
            _followUpReady = false;
            _followUpTimer = 0f;
            _followUpSkill = null;
            _followUpSlot  = SkillSlot.AttackA;
            _followUpCount = 0;
            UnsubscribeCurrentSkillHit();
            CleanupTelegraphs();
            StopAllCoroutines();
        }

        void OnDisable()
        {
            CleanupTelegraphs();
        }

        void CleanupTelegraphs()
        {
            for (int i = 0; i < _activeTelegraphs.Count; i++)
                if (_activeTelegraphs[i] != null)
                    Destroy(_activeTelegraphs[i]);
            _activeTelegraphs.Clear();
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
            SubscribeCurrentSkillHit();

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

        // ボス専用: 4枠(skills)＋追加技プール(extraSkills)から1つランダムに選んで発動する。
        // preferRanged=trueかつプール内にprojectile/beam系の技があればその中から選ぶ。
        public bool TryUseRandomSkill(bool preferRanged)
        {
            if (_isExecuting || !_fighter.CanAct) return false;

            var pool = new List<SkillData>();
            foreach (var s in skills) if (s != null) pool.Add(s);
            pool.AddRange(extraSkills);
            if (pool.Count == 0) return false;

            if (preferRanged)
            {
                var ranged = new List<SkillData>();
                foreach (var s in pool)
                {
                    if (s.actions == null) continue;
                    foreach (var a in s.actions)
                        if (a != null && (a.type == "projectile" || a.type == "beam")) { ranged.Add(s); break; }
                }
                if (ranged.Count > 0) pool = ranged;
            }

            // 直前と同じ技の連発を避ける（選択肢が他にあるときだけ除外。手数の多さを体感しやすくする）
            if (pool.Count > 1 && _lastRandomSkill != null)
            {
                var withoutLast = pool.FindAll(s => s != _lastRandomSkill);
                if (withoutLast.Count > 0) pool = withoutLast;
            }

            var chosen = pool[UnityEngine.Random.Range(0, pool.Count)];
            _lastRandomSkill = chosen;
            Debug.Log($"[BossAI] 選択: {chosen.skill_name} slot={chosen.slot} extraIdx={chosen.extraSpriteIndex} (poolSize={pool.Count}, preferRanged={preferRanged})");
            return TryUseDebugSkill(chosen);
        }

        IEnumerator ExecuteSkill(SkillData skill, float powerMultiplier)
        {
            _isExecuting = true;
            _voicePlayer?.PlaySkill(skill.slot);
            OnSkillExecuted?.Invoke(skill.slot);
            int serial = ++_skillSerial;
            _currentSkillHit = false;
            SubscribeCurrentSkillHit();
            float recovery = EffectiveRecovery(skill);
            float totalDuration = skill.parameters.startup + skill.parameters.active_time + recovery;
            float deferredActionsCompleteAt = 0f;
            float firstBeamTime = FirstActionTime(skill, "beam");
            if (firstBeamTime > 0f)
                totalDuration = Mathf.Max(totalDuration, firstBeamTime + skill.parameters.active_time + recovery);
            if (skill.actions != null)
            {
                for (int i = 0; i < skill.actions.Count; i++)
                {
                    var spatialAction = skill.actions[i];
                    if (spatialAction == null) continue;
                    float deferred = DeferredImpactDelay(spatialAction);
                    if (deferred <= 0f) continue;
                    deferredActionsCompleteAt = Mathf.Max(deferredActionsCompleteAt, spatialAction.time + deferred);
                    totalDuration = Mathf.Max(totalDuration,
                        spatialAction.time + deferred + skill.parameters.active_time + recovery);
                }
            }
            _fighter.BeginSkillRecovery(totalDuration);
            _fighter.ShowSkillWindup(skill, totalDuration);
            // スマッシュのオーラは溜め中(Fighter.UpdateSmashAura)で表示するため、発動時には出さない。
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
            _currentSkillEndTime = t0 + totalDuration;

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
                    if (IsImpactAction(a) && !ActionDefersImpact(a))
                        ShowImpactAtSpawn(skill);
                    ExecuteAction(skill, a, powerMultiplier);
                    actionIdx++;
                }
                else
                {
                    yield return null;
                }
            }

            while (Time.time - t0 < deferredActionsCompleteAt)
                yield return null;

            if (skill.follow_up_actions?.Count > 0)
                OpenFollowUpWindow(skill, 0);

            // recovery（後隙）が終わるまで待機
            while (Time.time - t0 < totalDuration) yield return null;

            _isExecuting = false;
            UnsubscribeCurrentSkillHit();
        }

        static bool IsImpactAction(SkillAction a)
        {
            if (a == null) return false;
            return a.type == "melee_hitbox" || a.type == "body_hitbox" ||
                   a.type == "area_hitbox" || a.type == "trap_hitbox" ||
                   a.type == "projectile" || a.type == "beam" ||
                   a.type == "jump_attack" || a.type == "uppercut" ||
                   a.type == "dive_attack" || a.type == "dash+melee_hitbox" ||
                   a.type == "multi_hit" || a.type == "summon" ||
                   a.type == "counter" || a.type == "reflector" ||
                   a.type == "command_throw" || a.type == "shockwave" ||
                   a.type == "gravity_well" || a.type == "lifesteal";
        }

        static bool ActionDefersImpact(SkillAction a)
        {
            if (a == null || !IsImpactAction(a)) return false;
            bool supportsTelegraph = a.type == "melee_hitbox" || a.type == "body_hitbox" ||
                                     a.type == "area_hitbox" || a.type == "trap_hitbox" ||
                                     a.type == "projectile" || a.type == "beam" ||
                                     a.type == "lifesteal" || a.type == "summon";
            if (supportsTelegraph && a.telegraph_time > 0f) return true;
            bool remoteOrigin = !string.IsNullOrEmpty(a.spawn_origin) && a.spawn_origin != "owner";
            if ((a.type == "melee_hitbox" || a.type == "body_hitbox" ||
                 a.type == "area_hitbox" || a.type == "lifesteal" || a.type == "summon") && remoteOrigin)
                return true;
            return a.type == "area_hitbox" && a.spawn_at_enemy;
        }

        static int PatternCountForTiming(SkillAction a)
        {
            if (a == null) return 1;
            int maxCount = a.type == "projectile" ? 10 : a.type == "summon" ? 6 : 4;
            int requested;
            if (a.pattern_count > 0) requested = a.pattern_count;
            else if ((a.type == "projectile" || a.type == "beam") && a.projectile_count > 1)
                requested = a.projectile_count;
            else requested = a.pattern switch
            {
                "mirrored" => 2,
                "parallel" => 3,
                "line"     => 3,
                "radial"   => 6,
                "inward"   => 6,
                "inward_ring" => 6,
                _ => 1,
            };
            return Mathf.Clamp(requested, 1, maxCount);
        }

        static float DeferredImpactDelay(SkillAction a)
        {
            float delay = 0f;
            if (ActionDefersImpact(a))
                delay = a.telegraph_time > 0f ? Mathf.Clamp(a.telegraph_time, 0.05f, 1.5f) : 0.4f;
            if (a != null && a.burst_interval > 0f)
                delay += Mathf.Clamp(a.burst_interval, 0f, 0.5f) * Mathf.Max(0, PatternCountForTiming(a) - 1);
            return delay;
        }

        void ShowImpactAtSpawn(SkillData skill)
        {
            if (_fighter == null || skill == null || _impactShownSerial == _skillSerial) return;
            _impactShownSerial = _skillSerial;
            _fighter.ShowSkillImpact(skill, Mathf.Max(0.05f, _currentSkillEndTime - Time.time));
        }

        void MarkCurrentSkillHit(float damage, bool wasBlocked)
        {
            _currentSkillHit = true;
        }

        void SubscribeCurrentSkillHit()
        {
            UnsubscribeCurrentSkillHit();
            if (_fighter == null || _fighter.Opponent == null) return;
            _skillHitSubscribedTo = _fighter.Opponent;
            _skillHitSubscribedTo.OnDamageReceived += MarkCurrentSkillHit;
        }

        void UnsubscribeCurrentSkillHit()
        {
            if (_skillHitSubscribedTo != null)
                _skillHitSubscribedTo.OnDamageReceived -= MarkCurrentSkillHit;
            _skillHitSubscribedTo = null;
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
                              a.type == "area_hitbox" || a.type == "jump_attack" || a.type == "beam" ||
                              a.type == "uppercut"    || a.type == "dive_attack";
                if (!melee) continue;
                hasMelee = true;
                float duration = a.duration > 0f ? a.duration : Mathf.Max(skill.parameters.active_time, 0.08f);
                latest = Mathf.Max(latest, a.time + DeferredImpactDelay(a) + duration);
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

        readonly struct SpatialSample
        {
            public readonly Vector2 position;
            public readonly Vector2 direction;
            public readonly float delay;

            public SpatialSample(Vector2 position, Vector2 direction, float delay)
            {
                this.position = position;
                this.direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
                this.delay = Mathf.Max(0f, delay);
            }

            public float Angle => Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        }

        bool HasExplicitSpatialOrigin(SkillAction a)
            => a != null && (!string.IsNullOrEmpty(a.spawn_origin) ||
                             !string.IsNullOrEmpty(a.spawn_anchor));

        static bool HasNewPattern(SkillAction a)
            => a != null && !string.IsNullOrEmpty(a.pattern) && a.pattern != "single";

        static bool HasSpatialOrientation(SkillAction a)
        {
            if (a == null) return false;
            if (!string.IsNullOrEmpty(a.aim_mode) || !Mathf.Approximately(a.rotation_angle, 0f) ||
                !Mathf.Approximately(a.projectile_angle, 0f)) return true;
            string shape = a.shape;
            return shape == "annulus" || shape == "arc" || shape == "line" ||
                   shape == "cross" || shape == "column" || shape == "cone";
        }

        Vector2 ResolveSpatialOrigin(SkillData skill, SkillAction a)
        {
            Vector2 ownerPos = _fighter != null ? (Vector2)_fighter.transform.position : Vector2.zero;
            Vector2 enemyPos = _fighter?.Opponent != null
                ? (Vector2)_fighter.Opponent.transform.position
                : ownerPos;
            var bm = Battle.BattleManager.Instance;
            float groundY = bm != null ? bm.StageGroundY : ownerPos.y;
            string origin = string.IsNullOrEmpty(a.spawn_origin) ? "owner" : a.spawn_origin;

            Vector2 point = origin switch
            {
                "enemy"       => enemyPos,
                "midpoint"    => (ownerPos + enemyPos) * 0.5f,
                "stage_center"=> new Vector2(
                    bm != null ? (bm.StageMinX + bm.StageMaxX) * 0.5f : 0f, groundY),
                "left_edge"   => new Vector2(bm != null ? bm.StageMinX + 0.35f : -5f, groundY),
                "right_edge"  => new Vector2(bm != null ? bm.StageMaxX - 0.35f : 5f, groundY),
                _             => ownerPos,
            };

            string anchor = string.IsNullOrEmpty(a.spawn_anchor) ? "auto" : a.spawn_anchor;
            float anchorScale = origin == "owner" ? _sizeScale : 1f;
            var enemyBody = origin == "enemy" && _fighter?.Opponent != null
                ? _fighter.Opponent.GetComponent<Collider2D>()
                : null;
            if (anchor == "body")
            {
                point.y = enemyBody != null ? enemyBody.bounds.center.y
                                            : point.y + 0.85f * anchorScale;
            }
            else if (anchor == "head")
            {
                point.y = enemyBody != null ? enemyBody.bounds.max.y
                                            : point.y + 1.65f * anchorScale;
            }
            else if (anchor == "weapon_tip" && origin == "owner")
            {
                var attackAnchor = AnchorFor(skill);
                if (attackAnchor.valid)
                {
                    float facing = _fighter.FacingRight ? 1f : -1f;
                    point += new Vector2(facing * attackAnchor.tip.x, attackAnchor.tip.y) * _sizeScale;
                }
            }
            // feet/auto はFighterの足元ピボットそのもの。

            float sign = _fighter != null && _fighter.FacingRight ? 1f : -1f;
            point += new Vector2(sign * a.spawn_x * _sizeScale, a.spawn_y * _sizeScale);
            if (bm != null)
                point.x = Mathf.Clamp(point.x, bm.StageMinX + 0.15f, bm.StageMaxX - 0.15f);
            return point;
        }

        Vector2 ResolveAimDirection(SkillAction a, Vector2 from, Vector2 fallback, float extraAngle = 0f)
        {
            Vector2 dir = fallback.sqrMagnitude > 0.0001f
                ? fallback.normalized
                : (_fighter != null && _fighter.FacingRight ? Vector2.right : Vector2.left);
            string aim = string.IsNullOrEmpty(a.aim_mode) ? "facing" : a.aim_mode;
            Vector2 enemyCenter = _fighter?.Opponent != null
                ? (Vector2)_fighter.Opponent.transform.position + Vector2.up * 0.8f
                : from + dir;
            var battleManager = Battle.BattleManager.Instance;
            Vector2 stageCenter = new Vector2(
                battleManager != null ? (battleManager.StageMinX + battleManager.StageMaxX) * 0.5f : 0f,
                battleManager != null ? battleManager.StageGroundY + 0.8f : from.y);

            switch (aim)
            {
                case "facing":
                    // 舞台端起点の既定方向は必ず舞台内側へ向ける。
                    if (a.spawn_origin == "left_edge") dir = Vector2.right;
                    else if (a.spawn_origin == "right_edge") dir = Vector2.left;
                    break;
                case "enemy":
                    dir = enemyCenter - from;
                    break;
                case "away_enemy":
                    dir = from - enemyCenter;
                    break;
                case "stage_center":
                    dir = stageCenter - from;
                    break;
                case "vector":
                    float sign = _fighter != null && _fighter.FacingRight ? 1f : -1f;
                    dir = new Vector2(sign * a.vector_x, a.vector_y);
                    break;
                case "radial_out":
                {
                    Vector2 radialCenter = _fighter != null
                        ? (Vector2)_fighter.transform.position + Vector2.up * 0.8f
                        : stageCenter;
                    dir = from - radialCenter;
                    break;
                }
                case "radial_in":
                {
                    Vector2 radialCenter = _fighter != null
                        ? (Vector2)_fighter.transform.position + Vector2.up * 0.8f
                        : stageCenter;
                    dir = radialCenter - from;
                    break;
                }
            }

            if (dir.sqrMagnitude < 0.0001f) dir = fallback.sqrMagnitude > 0.0001f ? fallback : Vector2.right;

            float angle = a.rotation_angle + extraAngle;
            if (aim == "facing" || aim == "vector")
            {
                // facing/vectorの角度はキャラクター基準。Xだけでなく回転方向も鏡映しないと、
                // 例: vector=(1,0.5), angle=26.565 が右では斜め上、左では真横になってしまう。
                // 舞台端起点のfacingだけはキャラの向きではなく、内側へ向けた実方向を基準にする。
                float mirrorSign;
                bool edgeFacing = aim == "facing" &&
                                  (a.spawn_origin == "left_edge" || a.spawn_origin == "right_edge");
                if (edgeFacing && Mathf.Abs(dir.x) > 0.0001f)
                    mirrorSign = Mathf.Sign(dir.x);
                else
                    mirrorSign = _fighter != null && _fighter.FacingRight ? 1f : -1f;
                angle *= mirrorSign;
            }
            return RotateVector(dir.normalized, angle);
        }

        static Vector2 RotateVector(Vector2 vector, float degrees)
        {
            if (Mathf.Approximately(degrees, 0f)) return vector;
            float rad = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            return new Vector2(vector.x * c - vector.y * s, vector.x * s + vector.y * c);
        }

        List<SpatialSample> BuildSpatialSamples(SkillAction a, Vector2 center, Vector2 baseDirection,
                                                bool projectile, int maxCount,
                                                Vector2 worldFootprint = default)
        {
            string pattern = string.IsNullOrEmpty(a.pattern) ? "single" : a.pattern;
            if (pattern == "inward_ring") pattern = "inward";
            if (pattern == "single" && projectile && a.projectile_count > 1)
                pattern = "fan"; // 旧 projectile_count の後方互換

            int defaultCount = pattern switch
            {
                "mirrored" => 2,
                "radial"   => 6,
                "inward"   => 6,
                "parallel" => 3,
                "line"     => 3,
                _          => projectile && a.projectile_count > 1 ? a.projectile_count : 1,
            };
            int requested = a.pattern_count > 0 ? a.pattern_count
                          : projectile && a.projectile_count > 1 ? a.projectile_count
                          : defaultCount;
            int count = Mathf.Clamp(requested, 1, Mathf.Max(1, maxCount));
            float spacing = Mathf.Clamp(a.pattern_spacing > 0f ? a.pattern_spacing : 0.8f, 0.2f, 3f) * _sizeScale;
            float defaultRadius = pattern == "mirrored" ? 1.1f : 2.5f;
            float radius = Mathf.Clamp(a.pattern_radius > 0f ? a.pattern_radius : defaultRadius, 0.5f, 6f) * _sizeScale;
            float interval = Mathf.Clamp(a.burst_interval, 0f, 0.5f);
            Vector2 direction = baseDirection.sqrMagnitude > 0.0001f ? baseDirection.normalized : Vector2.right;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            var samples = new List<SpatialSample>(count);

            for (int i = 0; i < count; i++)
            {
                float delay = interval * i;
                Vector2 position = center;
                Vector2 sampleDir = direction;
                switch (pattern)
                {
                    case "fan":
                    {
                        float step = a.spread_angle > 0f ? a.spread_angle : 15f;
                        sampleDir = RotateVector(direction, -step * (count - 1) * 0.5f + step * i);
                        break;
                    }
                    case "parallel":
                        position += perpendicular * ((i - (count - 1) * 0.5f) * spacing);
                        break;
                    case "radial":
                        sampleDir = RotateVector(direction, 360f * i / count);
                        break;
                    case "inward":
                    {
                        Vector2 radial = RotateVector(direction, 360f * i / count);
                        position += radial * radius;
                        sampleDir = -radial;
                        break;
                    }
                    case "mirrored":
                    {
                        float side = count == 1 ? 0f : Mathf.Lerp(-1f, 1f, i / (float)(count - 1));
                        position += Vector2.right * (side * radius);
                        // mirrored は中心の左右から外向きへ放つ。中心へ収束する配置は inward が担当する。
                        sampleDir = ResolveAimDirection(a, position, (position - center).normalized);
                        break;
                    }
                    case "line":
                        position += direction * ((i - (count - 1) * 0.5f) * spacing);
                        break;
                }
                bool duplicate = false;
                Vector2 normalizedDir = sampleDir.sqrMagnitude > 0.0001f ? sampleDir.normalized : direction;
                if (worldFootprint.x > 0f && worldFootprint.y > 0f)
                {
                    float angle = Mathf.Atan2(normalizedDir.y, normalizedDir.x) * Mathf.Rad2Deg;
                    position = ClampSpatialCenter(position, worldFootprint, angle);
                }
                else
                {
                    var bm = Battle.BattleManager.Instance;
                    if (bm != null)
                    {
                        position.x = Mathf.Clamp(position.x, bm.StageMinX + 0.15f, bm.StageMaxX - 0.15f);
                        position.y = Mathf.Clamp(position.y, bm.StageGroundY - 0.5f, bm.StageGroundY + 8f);
                    }
                }
                for (int j = 0; j < samples.Count; j++)
                {
                    if ((samples[j].position - position).sqrMagnitude <= 0.0025f &&
                        Vector2.Dot(samples[j].direction, normalizedDir) >= 0.985f)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                    samples.Add(new SpatialSample(position, normalizedDir, delay));
            }
            return samples;
        }

        static Vector2 ClampSpatialCenter(Vector2 center, Vector2 worldSize, float rotationDegrees)
        {
            var bm = Battle.BattleManager.Instance;
            if (bm == null) return center;
            float radians = rotationDegrees * Mathf.Deg2Rad;
            float c = Mathf.Abs(Mathf.Cos(radians));
            float s = Mathf.Abs(Mathf.Sin(radians));
            float hx = Mathf.Max(0.01f, worldSize.x) * 0.5f;
            float hy = Mathf.Max(0.01f, worldSize.y) * 0.5f;
            float extentX = c * hx + s * hy;
            float extentY = s * hx + c * hy;
            float minX = bm.StageMinX + extentX;
            float maxX = bm.StageMaxX - extentX;
            float minY = bm.StageGroundY - 0.5f + extentY;
            float maxY = bm.StageGroundY + 8f - extentY;
            center.x = minX <= maxX ? Mathf.Clamp(center.x, minX, maxX)
                                    : (bm.StageMinX + bm.StageMaxX) * 0.5f;
            center.y = minY <= maxY ? Mathf.Clamp(center.y, minY, maxY)
                                    : bm.StageGroundY + 3.75f;
            return center;
        }

        // ビームの根元を武器先/指定originへ固定したまま、舞台端までの距離に合わせて長さを縮める。
        // 中心を単純clampすると根元が後方へずれるため、中心線の終点と必要なら太さだけを調整する。
        static Vector2 FitBeamWorldSizeToStage(Vector2 origin, Vector2 direction, Vector2 desiredSize)
        {
            var bm = Battle.BattleManager.Instance;
            if (bm == null) return desiredSize;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            float minX = bm.StageMinX;
            float maxX = bm.StageMaxX;
            float minY = bm.StageGroundY - 0.5f;
            float maxY = bm.StageGroundY + 8f;

            // directionに直交する半幅が境界を越えない最大値。根元位置は動かさない。
            float halfThickness = Mathf.Max(0.01f, desiredSize.y * 0.5f);
            if (Mathf.Abs(direction.y) > 0.0001f)
            {
                halfThickness = Mathf.Min(halfThickness,
                    Mathf.Max(0f, origin.x - minX) / Mathf.Abs(direction.y),
                    Mathf.Max(0f, maxX - origin.x) / Mathf.Abs(direction.y));
            }
            if (Mathf.Abs(direction.x) > 0.0001f)
            {
                halfThickness = Mathf.Min(halfThickness,
                    Mathf.Max(0f, origin.y - minY) / Mathf.Abs(direction.x),
                    Mathf.Max(0f, maxY - origin.y) / Mathf.Abs(direction.x));
            }
            halfThickness = Mathf.Max(0.01f, halfThickness);
            float fittedHeight = Mathf.Min(desiredSize.y, halfThickness * 2f);
            float extentX = Mathf.Abs(direction.y) * fittedHeight * 0.5f;
            float extentY = Mathf.Abs(direction.x) * fittedHeight * 0.5f;

            float maxLength = Mathf.Max(0.05f, desiredSize.x);
            if (direction.x > 0.0001f)
                maxLength = Mathf.Min(maxLength, (maxX - extentX - origin.x) / direction.x);
            else if (direction.x < -0.0001f)
                maxLength = Mathf.Min(maxLength, (origin.x - (minX + extentX)) / -direction.x);
            if (direction.y > 0.0001f)
                maxLength = Mathf.Min(maxLength, (maxY - extentY - origin.y) / direction.y);
            else if (direction.y < -0.0001f)
                maxLength = Mathf.Min(maxLength, (origin.y - (minY + extentY)) / -direction.y);

            return new Vector2(Mathf.Clamp(maxLength, 0.05f, desiredSize.x), fittedHeight);
        }

        static bool PatternNeedsSharedHitGroup(SkillAction a)
        {
            if (a == null || string.IsNullOrEmpty(a.pattern)) return false;
            return a.pattern != "single";
        }

        int CreatePatternCastId(SkillAction a)
            => PatternNeedsSharedHitGroup(a) ? SkillCastHitRegistry.NextCastId() : 0;

        static float PatternHitLockSeconds(SkillAction a)
        {
            if (a == null) return 0.35f;
            float projectileLifetime = a.type == "projectile"
                ? (a.projectile_lifetime > 0f ? a.projectile_lifetime : 1.5f)
                : 0f;
            float implicitLifetime = a.type == "beam" ? 0.12f
                : a.type == "projectile" ? projectileLifetime
                : 0.75f; // melee/body/areaのactive_time上限(0.6s)＋余裕
            float lifetime = Mathf.Max(implicitLifetime, a.duration, projectileLifetime);
            float burstTail = Mathf.Clamp(a.burst_interval, 0f, 0.5f) *
                              Mathf.Max(0, PatternCountForTiming(a) - 1);
            return Mathf.Clamp(lifetime + burstTail + 0.15f, 0.35f, 6.5f);
        }

        void ConfigureSpatialHitbox(Hitbox hb, SkillAction a, Vector2 size, float directionAngle, int castId)
        {
            if (hb == null || a == null) return;
            string shape = string.IsNullOrEmpty(a.shape) ? "box" : a.shape;
            hb.DesiredWorldSize = size;
            // ringは内径0のannulusとして扱い、塗りつぶし円判定＋スポークのない円周表示にする。
            hb.SpatialShape = shape == "ring" ? "annulus" : shape;
            hb.SpatialInnerRadius = shape == "ring"
                ? 0f
                : Mathf.Max(0f, a.inner_radius) * _sizeScale * HitboxVisualScale;
            hb.SpatialArcAngle = shape == "ring" ? 360f
                : a.arc_angle > 0f ? Mathf.Clamp(a.arc_angle, 1f, 360f) : 90f;
            hb.SpatialCrossThickness = Mathf.Max(0f, a.inner_radius) * _sizeScale * HitboxVisualScale;
            hb.SharedCastId = castId;
            hb.SharedSourceId = SkillCastHitRegistry.NextSourceId();
            hb.SharedHitLockSeconds = PatternHitLockSeconds(a);
            hb.SpatialKnockbackMode = a.knockback_direction;
            hb.SpatialKnockbackOrigin = hb.transform.position;

            // beamは舞台端起点の既定facingでも左右が反転し得るため、常に実進行方向へ揃える。
            if (a.type == "beam" || HasSpatialOrientation(a) || HasNewPattern(a))
            {
                hb.transform.rotation = Quaternion.Euler(0f, 0f, directionAngle);
                hb.FlipEffectX = false;
            }
        }

        void ScheduleSpatial(float delay, System.Action action)
        {
            if (delay <= 0f) action?.Invoke();
            else StartCoroutine(InvokeSpatialAfter(delay, action));
        }

        IEnumerator InvokeSpatialAfter(float delay, System.Action action)
        {
            yield return new WaitForSeconds(delay);
            if (_fighter != null && _fighter.State != FighterState.Dead)
                action?.Invoke();
        }

        // アクション種 -> ハンドラのディスパッチ表。新しいアクションを足す際は
        // ここに1エントリ追加すればよく、中央のswitchを編集する必要がない。
        Dictionary<string, Action<SkillData, SkillAction, float>> _actionHandlers;

        void BuildActionHandlers()
        {
            _actionHandlers = new Dictionary<string, Action<SkillData, SkillAction, float>>
            {
                ["melee_hitbox"]       = SpawnMeleeHitbox,
                ["body_hitbox"]        = (skill, a, pm) => SpawnBodyHitbox(skill, a, pm),
                ["area_hitbox"]        = SpawnAreaHitbox,
                ["trap_hitbox"]        = SpawnTrapHitbox,
                ["projectile"]         = SpawnProjectile,
                ["beam"]               = SpawnBeam,
                ["jump_attack"]        = DoJumpAttack,
                ["uppercut"]           = DoUppercut,
                ["dive_attack"]        = DoDiveAttack,
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
                ["wall"]               = SpawnWall,
                ["apply_status"]       = (skill, a, pm) => ApplyOpponentStatus(a),
                ["heal_self"]          = (skill, a, pm) => HealSelf(a),
                ["barrier"]            = (skill, a, pm) => DoBarrier(a),
                ["command_throw"]      = DoCommandThrow,
                ["shockwave"]          = SpawnShockwave,
                ["gravity_well"]       = (skill, a, pm) => DoGravityWell(skill, a),
                ["lifesteal"]          = (skill, a, pm) => { if (a.lifesteal_ratio <= 0f) a.lifesteal_ratio = 0.3f; SpawnMeleeHitbox(skill, a, pm); },
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
            bool explicitOrigin = HasExplicitSpatialOrigin(a);
            float range   = a.range > 0f ? a.range : skill.parameters.range;
            if (range <= 0f) range = 1.2f;

            Vector2 baseOffset = DefaultMeleeOffset(skill.slot, range);
            float offsetX = a.spawn_x > 0f ? a.spawn_x : baseOffset.x;
            float offsetY = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : baseOffset.y;
            float height = a.size_y > 0f ? a.size_y : DefaultHitboxHeight(skill.slot);

            // 攻撃ポーズの得物（剣・槍・拳）の位置に判定を合わせる
            // （「剣を振っているのに判定が別の場所にある」対策）。
            var anchor = AnchorFor(skill);
            if (anchor.valid && !explicitOrigin)
            {
                if (anchor.weaponLength >= 0.35f)
                {
                    // 得物が写っている: 判定を「体の前端〜刃の先端」の帯に張り、見た目のリーチと一致させる
                    float startX = Mathf.Max(anchor.bodyEdgeX * 0.6f, 0.1f);
                    float endX   = Mathf.Max(anchor.tip.x + 0.15f, startX + range);
                    endX = Mathf.Min(endX, startX + range * 1.5f, 3.2f); // 伸ばしすぎ防止（バランス保護）
                    range   = endX - startX;
                    offsetX = (startX + endX) * 0.5f;
                    offsetY = Mathf.Clamp((0.95f + anchor.tip.y) * 0.5f, 0.6f, 1.5f);
                    height  = Mathf.Clamp(Mathf.Max(height, Mathf.Abs(anchor.tip.y - 0.95f) + 0.9f), 0.8f, 2.2f);
                }
                else
                {
                    // 素手・体術: 拳・蹴りの位置に判定を合わせ、「体の前端〜拳の少し先」の帯を張る。
                    // AIのspawn_xが大きすぎて体から離れた空中に判定・エフェクトが出るのを防ぎ、
                    // 同時にモーションの振り抜きぶんだけ拳より先へリーチを伸ばす。
                    offsetY = Mathf.Clamp(anchor.tip.y, 0.5f, 1.6f);
                    float startX = Mathf.Max(anchor.bodyEdgeX * 0.5f, 0.15f);
                    float endX   = Mathf.Max(anchor.tip.x + 0.35f, startX + Mathf.Min(range, 2.4f));
                    endX    = Mathf.Min(endX, startX + 2.6f);
                    range   = endX - startX;
                    offsetX = (startX + endX) * 0.5f;
                }
            }

            // エフェクトなし（キャラ本体判定）は視覚補助がないぶん広めに
            if (a.hide_effect) { range *= 1.3f; height *= 1.25f; }
            // キャラサイズに合わせてヒットボックスをスケール
            range   *= _sizeScale;
            height  *= _sizeScale;
            offsetX *= _sizeScale;
            offsetY *= _sizeScale;
            Vector2 offset = new Vector2(dirSign * offsetX, offsetY);
            Vector2 size   = new Vector2(range * HitboxVisualScale, height * HitboxVisualScale);
            Vector2 position = explicitOrigin
                ? ResolveSpatialOrigin(skill, a)
                : (Vector2)_fighter.transform.position + offset;
            Vector2 attackDir = ResolveAimDirection(a, position, new Vector2(dirSign, 0f));
            float lifetime = skill.parameters.active_time > 0f ? skill.parameters.active_time : 0.12f;
            if (skill.slot == SkillSlot.SmashSide) lifetime = Mathf.Max(lifetime, 0.15f);
            var samples = BuildSpatialSamples(a, position, attackDir, projectile: false, maxCount: 4,
                worldFootprint: size);
            int castId = CreatePatternCastId(a);

            void SpawnNow(SpatialSample sample)
            {
                ShowImpactAtSpawn(skill);
                var hb = Hitbox.Spawn(_fighter, sample.position, size, lifetime);
                hb.FollowOwner = a.follow_owner && samples.Count == 1 &&
                    (!explicitOrigin || string.IsNullOrEmpty(a.spawn_origin) || a.spawn_origin == "owner");
                Vector2 ownerDelta = sample.position - (Vector2)_fighter.transform.position;
                hb.OwnerLocalOffset = explicitOrigin
                    ? new Vector2(dirSign * ownerDelta.x, ownerDelta.y)
                    : new Vector2(offsetX, offsetY);
                float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage) *
                            powerMultiplier * _fighter.EffectiveDamageMultiplier;
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
                hb.EffectSprite   = a.hide_effect ? null : _fighter.GetEffectSprite(skill);
                hb.HideVisual     = a.hide_effect;
                hb.FlipEffectX    = !_fighter.FacingRight;
                hb.MaxHits        = a.hit_count > 0 ? a.hit_count : skill.parameters.hit_count;
                hb.IsSmashHit     = skill.slot == SkillSlot.SmashSide && powerMultiplier >= SkillConstants.SmashPowerThreshold;
                hb.LifestealRatio = Mathf.Clamp01(a.lifesteal_ratio);
                ApplyActionStatus(hb, a);
                ConfigureSpatialHitbox(hb, a, size, sample.Angle, castId);

                if (a.hide_effect)
                    Battle.SimpleFX.SwingArc(sample.position, sample.direction, size.x, size.y,
                        SkillEnumParser.ElementColor(skill.element));
            }

            bool remoteOrigin = explicitOrigin && !string.IsNullOrEmpty(a.spawn_origin) && a.spawn_origin != "owner";
            for (int i = 0; i < samples.Count; i++)
            {
                SpatialSample sample = samples[i];
                ScheduleSpatial(sample.delay, () =>
                {
                    if (remoteOrigin || a.telegraph_time > 0f)
                        StartCoroutine(TelegraphThenSpawn(sample.position, size, sample.Angle,
                            string.IsNullOrEmpty(a.shape) ? "box" : a.shape, skill.element,
                            a.telegraph_time > 0f ? Mathf.Clamp(a.telegraph_time, 0.05f, 1.5f) : 0.4f,
                            () => SpawnNow(sample),
                            a.inner_radius * _sizeScale * HitboxVisualScale, a.arc_angle));
                    else
                        SpawnNow(sample);
                });
            }
        }

        void SpawnBodyHitbox(SkillData skill, SkillAction a, float powerMultiplier, bool swingFx = true)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            bool explicitOrigin = HasExplicitSpatialOrigin(a);
            // spawn_x=0 はキャラ中心（前方オフセットなし）。>0 で前方に張り出す
            float width   = a.size_x > 0f ? a.size_x : (a.range > 0f ? a.range : 1.9f);
            float height  = a.size_y > 0f ? a.size_y : 2.3f; // デフォルトは全身
            float offsetX = a.spawn_x * _sizeScale;           // 0=体の中心、正値=前方
            float offsetY = (!Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.75f) * _sizeScale;
            width  *= _sizeScale;
            height *= _sizeScale;
            float lifetime = a.duration > 0f ? a.duration
                           : (skill.parameters.active_time > 0f ? skill.parameters.active_time : 0.28f);

            Vector2 legacyPosition = (Vector2)_fighter.transform.position + new Vector2(dirSign * offsetX, offsetY);
            Vector2 position = explicitOrigin ? ResolveSpatialOrigin(skill, a) : legacyPosition;
            Vector2 attackDir = ResolveAimDirection(a, position, new Vector2(dirSign, 0f));
            Vector2 worldSize = new Vector2(width * HitboxVisualScale, height * HitboxVisualScale);
            var samples = BuildSpatialSamples(a, position, attackDir, projectile: false, maxCount: 4,
                worldFootprint: worldSize);
            int castId = CreatePatternCastId(a);

            void SpawnNow(SpatialSample sample)
            {
                ShowImpactAtSpawn(skill);
                var hb = Hitbox.Spawn(_fighter, sample.position, worldSize, lifetime);
                hb.FollowOwner = samples.Count == 1 &&
                    (!explicitOrigin || string.IsNullOrEmpty(a.spawn_origin) || a.spawn_origin == "owner");
                Vector2 ownerDelta = sample.position - (Vector2)_fighter.transform.position;
                hb.OwnerLocalOffset = explicitOrigin
                    ? new Vector2(dirSign * ownerDelta.x, ownerDelta.y)
                    : new Vector2(offsetX, offsetY);
                hb.HideVisual = true;
                float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage)
                            * powerMultiplier * _fighter.EffectiveDamageMultiplier;
                hb.Damage = dmg;
                hb.DamageIncludesOwnerBoost = true;
                hb.Knockback = skill.parameters.knockback * powerMultiplier;
                var (kbDir, kbFixed) = ComputeKnockback(a, 1f, 0.3f);
                hb.KnockbackDir = kbDir;
                hb.FixedKnockbackDir = kbFixed;
                hb.GroundBounce = a.knockback_direction == "ground_bounce";
                hb.StunTime = skill.parameters.stun_time;
                hb.GuardDamage = skill.parameters.guard_damage;
                hb.Element = skill.element;
                hb.MaxHits = a.hit_count > 0 ? a.hit_count : skill.parameters.hit_count;
                ApplyActionStatus(hb, a);
                ConfigureSpatialHitbox(hb, a, worldSize, sample.Angle, castId);

                if (swingFx)
                    Battle.SimpleFX.SwingArc(sample.position,
                        sample.direction, worldSize.x, worldSize.y,
                        SkillEnumParser.ElementColor(skill.element));
            }

            bool remoteOrigin = explicitOrigin && !string.IsNullOrEmpty(a.spawn_origin) && a.spawn_origin != "owner";
            for (int i = 0; i < samples.Count; i++)
            {
                SpatialSample sample = samples[i];
                ScheduleSpatial(sample.delay, () =>
                {
                    if (remoteOrigin || a.telegraph_time > 0f)
                        StartCoroutine(TelegraphThenSpawn(sample.position, worldSize, sample.Angle,
                            string.IsNullOrEmpty(a.shape) ? "box" : a.shape, skill.element,
                            a.telegraph_time > 0f ? Mathf.Clamp(a.telegraph_time, 0.05f, 1.5f) : 0.4f,
                            () => SpawnNow(sample),
                            a.inner_radius * _sizeScale * HitboxVisualScale, a.arc_angle));
                    else
                        SpawnNow(sample);
                });
            }
        }

        void SpawnAreaHitbox(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign  = _fighter.FacingRight ? 1f : -1f;
            float lifetime = a.duration > 0f ? a.duration : Mathf.Max(skill.parameters.active_time, 0.12f);
            string shape   = string.IsNullOrEmpty(a.shape) ? "box" : a.shape;
            bool explicitOrigin = HasExplicitSpatialOrigin(a);
            // 相手の位置に発生（落雷・地割れ・間欠泉）。発動位置は今の相手位置で固定し、
            // 0.4秒の警告マーカーの後に判定を出す（移動すれば避けられる読み合いにする）。
            bool atEnemy = a.spawn_at_enemy && _fighter.Opponent != null;
            Vector2 basePos = atEnemy
                ? (Vector2)_fighter.Opponent.transform.position
                : (Vector2)_fighter.transform.position;

            // ringは内径0のannulusとして可視アウトライン＋円形フィルタへ統一する。
            // 各shapeのbroad-phase寸法。annulus/arc/crossはHitbox側の幾何フィルタで
            // 可視領域と同じ形へ絞り込む。line/columnは回転BoxCollider自体が最終形状。
            float width, height, offsetX, offsetY2;
            if (shape == "annulus" || shape == "arc" || shape == "ring")
            {
                float diameter = a.size_x > 0f ? a.size_x : (a.range > 0f ? a.range : 2f);
                width = height = diameter;
                offsetX = !Mathf.Approximately(a.spawn_x, 0f) ? a.spawn_x : 0f;
                offsetY2 = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.75f;
            }
            else if (shape == "line")
            {
                width = a.size_x > 0f ? a.size_x : (a.range > 0f ? a.range : 3.2f);
                height = a.size_y > 0f ? a.size_y : 0.35f;
                offsetX = !Mathf.Approximately(a.spawn_x, 0f) ? a.spawn_x : width * 0.5f;
                offsetY2 = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.8f;
            }
            else if (shape == "column")
            {
                // ローカルXを柱の長軸として照準方向へ向ける。size_x=太さ、size_y/range=長さ。
                // これによりaim=(0,1)で縦柱、斜めaimで斜め柱になり、transform.rightも攻撃方向と一致する。
                width = a.size_y > 0f ? a.size_y : (a.range > 0f ? a.range : 3.4f);
                height = a.size_x > 0f ? a.size_x : 0.8f;
                offsetX = !Mathf.Approximately(a.spawn_x, 0f) ? a.spawn_x : 1.8f;
                offsetY2 = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0f;
            }
            else if (shape == "cross")
            {
                width = a.size_x > 0f ? a.size_x : (a.range > 0f ? a.range : 3f);
                height = a.size_y > 0f ? a.size_y : width;
                offsetX = !Mathf.Approximately(a.spawn_x, 0f) ? a.spawn_x : 1.5f;
                offsetY2 = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.9f;
            }
            else if (shape == "cone")
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

            Vector2 center;
            if (explicitOrigin)
                center = ResolveSpatialOrigin(skill, a);
            else
                center = basePos + new Vector2(atEnemy ? 0f : dirSign * offsetX, offsetY2);
            Vector2 fallbackDirection = shape == "column" ? Vector2.up : new Vector2(dirSign, 0f);
            Vector2 baseDirection = ResolveAimDirection(a, center, fallbackDirection);
            // columnのoriginは中心ではなく根元。長軸の半分だけ進めて、足元/発生点から伸ばす。
            if (shape == "column") center += baseDirection * (width * 0.5f);
            Vector2 hitboxSize = new Vector2(width, height);
            Vector2 worldHitboxSize = hitboxSize * HitboxVisualScale;
            var samples = BuildSpatialSamples(a, center, baseDirection, projectile: false, maxCount: 4,
                worldFootprint: worldHitboxSize);
            int castId = CreatePatternCastId(a);
            bool warn = atEnemy || a.spawn_origin == "enemy" || a.telegraph_time > 0f;
            float warningSeconds = a.telegraph_time > 0f ? Mathf.Clamp(a.telegraph_time, 0.05f, 1.5f) : 0.4f;

            for (int i = 0; i < samples.Count; i++)
            {
                SpatialSample sample = samples[i];
                void SpawnNow()
                {
                    ShowImpactAtSpawn(skill);
                    var hbox = SpawnConfiguredHitbox(
                        skill, a, powerMultiplier, sample.position, hitboxSize, lifetime);
                    bool ownerOrigin = string.IsNullOrEmpty(a.spawn_origin) || a.spawn_origin == "owner";
                    hbox.FollowOwner = !atEnemy && ownerOrigin && a.follow_owner && samples.Count == 1;
                    if (hbox.FollowOwner)
                    {
                        Vector2 delta = sample.position - (Vector2)_fighter.transform.position;
                        hbox.OwnerLocalOffset = new Vector2(dirSign * delta.x, delta.y);
                    }
                    ConfigureSpatialHitbox(hbox, a, worldHitboxSize, sample.Angle, castId);
                    if (shape == "cone" || shape == "arc")
                        hbox.SetDebugColor(new Color(0.3f, 0.9f, 1f, 0.6f));
                }

                ScheduleSpatial(sample.delay, () =>
                {
                    if (warn)
                        StartCoroutine(TelegraphThenSpawn(sample.position, worldHitboxSize, sample.Angle,
                            shape, skill.element, warningSeconds, SpawnNow,
                            a.inner_radius * _sizeScale * HitboxVisualScale, a.arc_angle));
                    else
                        SpawnNow();
                });
            }
        }

        // 相手の位置に発生する技の警告表示。属性色のマーカーを点滅させてから発動する。
        IEnumerator TelegraphThenSpawn(Vector2 pos, float diameter, Element element, float delay, System.Action spawnAction)
        {
            var go = new GameObject("SkillTelegraph");
            _activeTelegraphs.Add(go);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprite.TelegraphRadial();
            sr.sortingOrder = 6;
            Color c = SkillEnumParser.ElementColor(element);
            Vector2 ss = sr.sprite.bounds.size;
            if (ss.x > 0f && ss.y > 0f)
                go.transform.localScale = new Vector3(diameter / ss.x, diameter / ss.y, 1f);

            float t = 0f;
            while (t < delay)
            {
                t += Time.deltaTime;
                float blink = 0.5f + 0.5f * Mathf.Sin(t * 28f);
                sr.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.16f, 0.5f, blink));
                yield return null;
            }
            _activeTelegraphs.Remove(go);
            Destroy(go);
            if (_fighter != null && _fighter.State != FighterState.Dead)
                spawnAction?.Invoke();
        }

        IEnumerator TelegraphThenSpawn(Vector2 pos, Vector2 size, float rotation, string shape,
                                       Element element, float delay, System.Action spawnAction,
                                       float innerRadius = 0f, float arcAngle = 0f)
        {
            var go = new GameObject("SkillTelegraph");
            _activeTelegraphs.Add(go);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
            Color c = SkillEnumParser.ElementColor(element);
            SpriteRenderer sr = null;
            var lines = new List<LineRenderer>(2);
            bool outlineShape = shape == "annulus" || shape == "arc" || shape == "ring" ||
                                shape == "cross" || shape == "cone";
            if (!outlineShape)
            {
                sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = shape == "line"
                    ? RuntimeSprite.TelegraphLine()
                    : RuntimeSprite.TelegraphBox();
                sr.sortingOrder = 6;
                Vector2 ss = sr.sprite.bounds.size;
                if (ss.x > 0f && ss.y > 0f)
                    go.transform.localScale = new Vector3(size.x / ss.x, size.y / ss.y, 1f);
            }
            else
            {
                LineRenderer outer = CreateTelegraphLine(go.transform, "OutlineA", c);
                lines.Add(outer);
                if (shape == "annulus" || shape == "ring")
                {
                    float outerRadius = Mathf.Min(size.x, size.y) * 0.5f;
                    SetTelegraphCircle(outer, outerRadius);
                    if (shape == "annulus" && innerRadius > 0.01f)
                    {
                        LineRenderer inner = CreateTelegraphLine(go.transform, "OutlineB", c);
                        SetTelegraphCircle(inner, Mathf.Min(innerRadius, outerRadius - 0.02f));
                        lines.Add(inner);
                    }
                }
                else if (shape == "arc")
                    SetTelegraphArc(outer, Mathf.Min(size.x, size.y) * 0.5f,
                        innerRadius, arcAngle > 0f ? arcAngle : 90f);
                else if (shape == "cross")
                    SetTelegraphCross(outer, size.x, size.y,
                        innerRadius > 0f ? innerRadius : Mathf.Min(size.x, size.y) * 0.3f);
                else
                    SetTelegraphCone(outer, size.x, size.y);
            }

            float t = 0f;
            while (t < delay)
            {
                t += Time.deltaTime;
                float blink = 0.5f + 0.5f * Mathf.Sin(t * 28f);
                float alpha = Mathf.Lerp(0.18f, 0.72f, blink);
                Color pulse = new Color(c.r, c.g, c.b, alpha);
                if (sr != null) sr.color = new Color(c.r, c.g, c.b, alpha * 0.58f);
                for (int i = 0; i < lines.Count; i++)
                {
                    lines[i].startColor = pulse;
                    lines[i].endColor = pulse;
                }
                yield return null;
            }
            _activeTelegraphs.Remove(go);
            Destroy(go);
            if (_fighter != null && _fighter.State != FighterState.Dead)
                spawnAction?.Invoke();
        }

        static LineRenderer CreateTelegraphLine(Transform parent, string objectName, Color color)
        {
            if (s_telegraphLineMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    s_telegraphLineMaterial = new Material(shader) { name = "SkillTelegraphOutline" };
                    Sprite outlineSprite = RuntimeSprite.TelegraphLine();
                    if (outlineSprite != null)
                        s_telegraphLineMaterial.mainTexture = outlineSprite.texture;
                }
            }
            var lineGo = new GameObject(objectName);
            lineGo.transform.SetParent(parent, false);
            var line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Tile;
            line.sortingOrder = 6;
            line.startWidth = 0.065f;
            line.endWidth = 0.065f;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 2;
            if (s_telegraphLineMaterial != null) line.sharedMaterial = s_telegraphLineMaterial;
            return line;
        }

        static void SetTelegraphCircle(LineRenderer line, float radius)
        {
            const int segments = 40;
            line.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
        }

        static void SetTelegraphArc(LineRenderer line, float outerRadius, float innerRadius, float arcDegrees)
        {
            const int segments = 24;
            float half = Mathf.Clamp(arcDegrees, 1f, 360f) * 0.5f;
            innerRadius = Mathf.Clamp(innerRadius, 0f, Mathf.Max(0f, outerRadius - 0.02f));
            int innerPoints = innerRadius > 0.01f ? segments + 1 : 1;
            line.positionCount = segments + 1 + innerPoints;
            int index = 0;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-half, half, i / (float)segments) * Mathf.Deg2Rad;
                line.SetPosition(index++, new Vector3(Mathf.Cos(angle) * outerRadius, Mathf.Sin(angle) * outerRadius, 0f));
            }
            if (innerRadius > 0.01f)
            {
                for (int i = segments; i >= 0; i--)
                {
                    float angle = Mathf.Lerp(-half, half, i / (float)segments) * Mathf.Deg2Rad;
                    line.SetPosition(index++, new Vector3(Mathf.Cos(angle) * innerRadius, Mathf.Sin(angle) * innerRadius, 0f));
                }
            }
            else line.SetPosition(index, Vector3.zero);
        }

        static void SetTelegraphCross(LineRenderer line, float width, float height, float thickness)
        {
            float hx = width * 0.5f;
            float hy = height * 0.5f;
            float t = Mathf.Clamp(thickness * 0.5f, 0.02f, Mathf.Min(hx, hy));
            Vector3[] points =
            {
                new Vector3(-t,hy,0f), new Vector3(t,hy,0f), new Vector3(t,t,0f), new Vector3(hx,t,0f),
                new Vector3(hx,-t,0f), new Vector3(t,-t,0f), new Vector3(t,-hy,0f), new Vector3(-t,-hy,0f),
                new Vector3(-t,-t,0f), new Vector3(-hx,-t,0f), new Vector3(-hx,t,0f), new Vector3(-t,t,0f),
            };
            line.positionCount = points.Length;
            line.SetPositions(points);
        }

        static void SetTelegraphCone(LineRenderer line, float width, float height)
        {
            float hx = width * 0.5f;
            float hy = height * 0.5f;
            line.positionCount = 3;
            line.SetPosition(0, new Vector3(-hx, 0f, 0f));
            line.SetPosition(1, new Vector3(hx, hy, 0f));
            line.SetPosition(2, new Vector3(hx, -hy, 0f));
        }

        void SpawnTrapHitbox(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            bool explicitOrigin = HasExplicitSpatialOrigin(a);
            float width = a.size_x > 0f ? a.size_x : Mathf.Max(0.8f, a.range > 0f ? a.range : skill.parameters.range);
            float height = a.size_y > 0f ? a.size_y : 0.9f;
            float offsetX = !Mathf.Approximately(a.spawn_x, 0f) ? a.spawn_x : width * 0.8f;
            float offsetY = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.35f;
            width   *= _sizeScale;
            height  *= _sizeScale;
            float lifetime = a.duration > 0f ? a.duration : Mathf.Max(skill.parameters.active_time, 0.35f);

            // 設置技は基本的に地面へ置く（空中に浮いた地雷を防ぐ）。
            // spawn_y が高い場合のみ「空中トラップ」としてAI指定を尊重する。
            Vector2 center;
            if (explicitOrigin)
            {
                center = ResolveSpatialOrigin(skill, a);
                string anchorName = string.IsNullOrEmpty(a.spawn_anchor) ? "auto" : a.spawn_anchor;
                if (a.spawn_y < 1.5f && (anchorName == "auto" || anchorName == "feet"))
                {
                    var floorHit = Physics2D.Raycast(center + Vector2.up * 0.8f,
                        Vector2.down, 10f, _fighter.groundLayer);
                    if (floorHit.collider != null)
                        center.y = floorHit.point.y + height * HitboxVisualScale * 0.5f;
                }
            }
            else if (offsetY >= 1.5f)
            {
                center = (Vector2)_fighter.transform.position + new Vector2(dirSign * offsetX, offsetY);
            }
            else
            {
                // spawn_at_enemy: 相手の足元に設置（アーム時間0.25秒＋脈動表示が回避猶予になる）
                float trapX = a.spawn_at_enemy && _fighter.Opponent != null
                    ? _fighter.Opponent.transform.position.x
                    : _fighter.transform.position.x + dirSign * offsetX;
                // 設置位置の床をレイキャストで探す（キャラのピボット＝足元。StageGroundYはステージに
                // よって見た目の床と一致しないため使わない）。見つからなければ使用者の足元の高さ。
                float groundRef = _fighter.transform.position.y;
                var hit = Physics2D.Raycast(
                    new Vector2(trapX, _fighter.transform.position.y + 0.6f),
                    Vector2.down, 10f, _fighter.groundLayer);
                if (hit.collider != null) groundRef = hit.point.y;
                center = new Vector2(trapX, groundRef + height * HitboxVisualScale * 0.5f);
            }

            Vector2 baseDirection = ResolveAimDirection(a, center, new Vector2(dirSign, 0f));
            Vector2 hitboxSize = new Vector2(width, height);
            Vector2 worldHitboxSize = hitboxSize * HitboxVisualScale;
            var samples = BuildSpatialSamples(a, center, baseDirection, projectile: false, maxCount: 4,
                worldFootprint: worldHitboxSize);
            int castId = CreatePatternCastId(a);
            for (int i = 0; i < samples.Count; i++)
            {
                SpatialSample sample = samples[i];
                void SpawnNow()
                {
                    ShowImpactAtSpawn(skill);
                    var hb = SpawnConfiguredHitbox(
                        skill, a, powerMultiplier, sample.position, hitboxSize, lifetime);
                    hb.IsTrap  = true;
                    hb.ArmTime = 0.25f;
                    ConfigureSpatialHitbox(hb, a, worldHitboxSize, sample.Angle, castId);
                }

                ScheduleSpatial(sample.delay, () =>
                {
                    if (a.telegraph_time > 0f)
                        StartCoroutine(TelegraphThenSpawn(sample.position, worldHitboxSize, sample.Angle,
                            string.IsNullOrEmpty(a.shape) ? "box" : a.shape, skill.element,
                            Mathf.Clamp(a.telegraph_time, 0.05f, 1.5f), SpawnNow,
                            a.inner_radius * _sizeScale * HitboxVisualScale, a.arc_angle));
                    else
                        SpawnNow();
                });
            }
        }

        Hitbox SpawnConfiguredHitbox(SkillData skill, SkillAction a, float powerMultiplier,
                                     Vector2 position, Vector2 size, float lifetime)
        {
            size *= HitboxVisualScale;
            var hb = Hitbox.Spawn(_fighter, position, size, lifetime);
            float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage) *
                        powerMultiplier * _fighter.EffectiveDamageMultiplier;
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
            hb.EffectSprite   = a.hide_effect ? null : _fighter.GetEffectSprite(skill);
            hb.HideVisual     = a.hide_effect;
            hb.FlipEffectX    = !_fighter.FacingRight;
            hb.MaxHits        = a.hit_count > 0 ? a.hit_count : skill.parameters.hit_count;
            ApplyActionStatus(hb, a);
            return hb;
        }

        // 攻撃ポーズのスプライト解析アンカー（武器の先端・銃口・拳の位置）。
        AttackAnchor AnchorFor(SkillData skill)
            => AttackAnchorEstimator.Get(_fighter.GetAttackPoseSprite(skill));

        void SpawnProjectile(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            bool explicitOrigin = HasExplicitSpatialOrigin(a);
            Vector2 baseOffset = DefaultProjectileOffset(skill.slot);
            float offsetX = a.spawn_x > 0f ? a.spawn_x : baseOffset.x;
            float offsetY = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : baseOffset.y;

            // 発射点を攻撃ポーズの銃口・杖先・拳の位置に合わせる（「銃の少し下から弾が出る」対策）。
            // 落下弾・山なり弾など特殊軌道（重力・大角度・上空スポーン）はAI指定の軌道を尊重する。
            bool specialTrajectory = !Mathf.Approximately(a.gravity_scale, 0f)
                                     || Mathf.Abs(a.projectile_angle) >= 30f
                                     || offsetY >= 2f;
            var anchor = AnchorFor(skill);
            if (anchor.valid && !specialTrajectory && !explicitOrigin)
            {
                offsetX = Mathf.Max(anchor.tip.x + 0.12f, 0.35f);
                offsetY = Mathf.Clamp(anchor.tip.y, 0.3f, 2.0f);
            }
            // キャラの表示サイズに合わせて発射点もスケール（巨大化/縮小時のずれ防止）
            offsetX *= _sizeScale;
            offsetY *= _sizeScale;
            Vector2 spawn = explicitOrigin
                ? ResolveSpatialOrigin(skill, a)
                : (Vector2)_fighter.transform.position + new Vector2(dirSign * offsetX, offsetY);

            // 相手の位置に発生（落雷・隕石）: 相手の頭上から落とす。水平角のままだと回避不能なので落下弾化する。
            float baseAngle = a.projectile_angle;
            if (!explicitOrigin && a.spawn_at_enemy && _fighter.Opponent != null)
            {
                Vector2 ep = _fighter.Opponent.transform.position;
                float dropH = Mathf.Clamp(offsetY < 2f ? 3.5f : offsetY, 2.5f, 6f);
                spawn = new Vector2(ep.x, ep.y + dropH);
                if (baseAngle > -30f) baseAngle = -90f;
            }

            Vector2 baseDirection;
            if (!string.IsNullOrEmpty(a.aim_mode) || explicitOrigin || !Mathf.Approximately(a.rotation_angle, 0f))
            {
                baseDirection = ResolveAimDirection(a, spawn, new Vector2(dirSign, 0f), baseAngle);
            }
            else
            {
                float baseRad = baseAngle * Mathf.Deg2Rad;
                baseDirection = new Vector2(dirSign * Mathf.Cos(baseRad), Mathf.Sin(baseRad)).normalized;
            }

            float speed    = a.projectile_speed    > 0f ? a.projectile_speed    : 9f;
            float lifetime = a.projectile_lifetime > 0f ? a.projectile_lifetime : 1.5f;
            float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage) *
                        powerMultiplier * _fighter.EffectiveDamageMultiplier;
            Vector2 desiredSize = new Vector2(
                (a.size_x > 0f ? a.size_x : Mathf.Clamp(speed * lifetime * 0.11f, 1.05f, 2.3f)) * HitboxVisualScale * _sizeScale,
                (a.size_y > 0f ? a.size_y : 1.05f) * HitboxVisualScale * _sizeScale);
            var (kbDir, kbFixed) = ComputeKnockback(a, 1f, 0.3f);

            var samples = BuildSpatialSamples(a, spawn, baseDirection, projectile: true, maxCount: 10,
                worldFootprint: desiredSize);
            int castId = CreatePatternCastId(a);
            for (int i = 0; i < samples.Count; i++)
            {
                SpatialSample sample = samples[i];
                void SpawnNow()
                {
                    ShowImpactAtSpawn(skill);
                    var p = Projectile.Spawn(_fighter, sample.position, sample.direction, speed, lifetime);
                    p.Damage                   = dmg;
                    p.DamageIncludesOwnerBoost = true;
                    p.Knockback                = skill.parameters.knockback * powerMultiplier;
                    p.KnockbackDir             = kbDir;
                    p.FixedKnockbackDir        = kbFixed;
                    p.GroundBounce             = a.knockback_direction == "ground_bounce";
                    p.StunTime                 = skill.parameters.stun_time;
                    p.GuardDamage              = skill.parameters.guard_damage;
                    p.Status                   = SkillEnumParser.ParseStatus(a.status);
                    p.StatusDuration           = a.status_duration > 0f ? a.status_duration : a.duration;
                    p.StatusChance             = Mathf.Clamp01(a.chance);
                    p.Element                  = skill.element;
                    p.EffectSprite             = a.hide_effect ? null : _fighter.GetEffectSprite(skill);
                    p.HideVisual               = a.hide_effect;
                    p.AlignToVelocity          = HasSpatialOrientation(a) || HasNewPattern(a) ||
                                                 Mathf.Abs(sample.direction.y) > 0.001f;
                    p.FlipEffectX              = !p.AlignToVelocity && !_fighter.FacingRight;
                    p.DesiredWorldSize         = desiredSize;
                    p.GravityScale             = a.gravity_scale;
                    p.IsBoomerang              = a.boomerang;
                    p.ExplosionRadius          = a.explosion_radius;
                    p.BounceCount              = a.bounce_count;
                    p.WaveAmplitude            = a.wave_amplitude;
                    p.Pierce                   = a.pierce;
                    p.SplitCount               = a.split_count;
                    p.SplitAngle               = a.split_angle > 0f ? a.split_angle : 30f;
                    p.SharedCastId             = castId;
                    p.SharedSourceId           = SkillCastHitRegistry.NextSourceId();
                    p.SharedHitLockSeconds     = PatternHitLockSeconds(a);
                    p.SpatialKnockbackMode     = a.knockback_direction;
                    p.SpatialKnockbackOrigin   = sample.position;
                    if (a.orbit)
                    {
                        // 衛星弾: 周回半径=range、周回速度=projectile_speed。敵は貫通扱いで1体1ヒット
                        p.OrbitOwner  = true;
                        p.OrbitRadius = Mathf.Clamp(a.range > 0f ? a.range : 1.6f, 0.8f, 3f) * _sizeScale;
                        p.Pierce      = true;
                    }
                    if ((a.homing || a.homing_strength > 0f) && _fighter.Opponent != null)
                    {
                        p.HomingTarget   = _fighter.Opponent.transform;
                        p.HomingStrength = a.homing_strength > 0f ? a.homing_strength : 0.5f;
                    }
                }

                ScheduleSpatial(sample.delay, () =>
                {
                    if (a.telegraph_time > 0f)
                        StartCoroutine(TelegraphThenSpawn(sample.position, desiredSize, sample.Angle,
                            "line", skill.element, Mathf.Clamp(a.telegraph_time, 0.05f, 1.5f), SpawnNow));
                    else
                        SpawnNow();
                });
            }
        }

        void SpawnBeam(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            bool explicitOrigin = HasExplicitSpatialOrigin(a);
            float width   = a.size_x > 0f ? a.size_x : (a.range > 0f ? a.range : 7f);
            float height  = a.size_y > 0f ? a.size_y : 0.5f;
            height = Mathf.Max(height, Mathf.Clamp(width * 0.1f, 0.55f, 1.2f));

            // ビームは「起点（銃口・掌の位置）から前方へ伸びる」ように配置する。
            // 従来は spawn_x をビーム中心として扱っていたため、幅が広いほど後ろ半分が
            // キャラの背後へはみ出していた（「ビームが自分の後ろから出る」の原因）。
            var anchor = AnchorFor(skill);
            Vector2 origin;
            if (explicitOrigin)
            {
                origin = ResolveSpatialOrigin(skill, a);
            }
            else if (anchor.valid)
            {
                float originX = Mathf.Max(anchor.tip.x - 0.1f, 0.2f) * _sizeScale;
                float originY = Mathf.Clamp(anchor.tip.y, 0.4f, 1.8f) * _sizeScale;
                origin = (Vector2)_fighter.transform.position + new Vector2(dirSign * originX, originY);
            }
            else
            {
                float originX = (a.spawn_x > 0f ? a.spawn_x : 0.55f) * _sizeScale;
                float originY = (!Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.8f) * _sizeScale;
                origin = (Vector2)_fighter.transform.position + new Vector2(dirSign * originX, originY);
            }

            width   *= _sizeScale;
            height  *= _sizeScale;
            float lifetime = a.duration > 0f ? a.duration : 0.07f;
            Vector2 direction = ResolveAimDirection(a, origin, new Vector2(dirSign, 0f), a.projectile_angle);
            var samples = BuildSpatialSamples(a, origin, direction, projectile: true, maxCount: 4);
            int castId = CreatePatternCastId(a);
            Vector2 beamSize = new Vector2(width, height);
            Vector2 worldBeamSize = beamSize * HitboxVisualScale;

            for (int i = 0; i < samples.Count; i++)
            {
                SpatialSample sample = samples[i];
                Vector2 fittedWorldBeamSize = FitBeamWorldSizeToStage(
                    sample.position, sample.direction, worldBeamSize);
                Vector2 fittedBeamSize = fittedWorldBeamSize / HitboxVisualScale;
                Vector2 beamCenter = sample.position + sample.direction * (fittedWorldBeamSize.x * 0.5f);
                void SpawnNow()
                {
                    ShowImpactAtSpawn(skill);
                    var hb = SpawnConfiguredHitbox(
                        skill, a, powerMultiplier, beamCenter, fittedBeamSize, lifetime);
                    hb.MaxHits = a.hit_count > 1 ? a.hit_count : 5;
                    ConfigureSpatialHitbox(hb, a, fittedWorldBeamSize, sample.Angle, castId);
                }

                ScheduleSpatial(sample.delay, () =>
                {
                    if (a.telegraph_time > 0f)
                        StartCoroutine(TelegraphThenSpawn(beamCenter, fittedWorldBeamSize, sample.Angle,
                            "line", skill.element, Mathf.Clamp(a.telegraph_time, 0.05f, 1.5f), SpawnNow));
                    else
                        SpawnNow();
                });
            }
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
            // Y はキャラの頭〜胴の高さ。低すぎると地面すれすれに見えるため、頭付近に合わせる。
            return slot switch
            {
                SkillSlot.AttackA => new Vector2(x, 0.95f),
                SkillSlot.AttackB => new Vector2(x, 1.15f),
                SkillSlot.AttackC => new Vector2(x + 0.15f, 0.8f),
                SkillSlot.SmashSide => new Vector2(x + 0.25f, 1.05f),
                _ => new Vector2(x, 0.95f),
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
            float power = a.power > 0f ? a.power : 5f;
            bool spatialAim = !string.IsNullOrEmpty(a.aim_mode) || !Mathf.Approximately(a.rotation_angle, 0f);
            if (spatialAim)
            {
                Vector2 direction = ResolveAimDirection(a, _fighter.transform.position,
                    new Vector2(dirSign, 0f));
                if (a.direction == "backward") direction = -direction;
                _fighter.ApplyImpulse(direction * power);
                _fighter.TriggerDashDust(Mathf.Sign(direction.x) == 0f ? dirSign : Mathf.Sign(direction.x));
                return;
            }

            if (a.direction == "backward") dirSign = -dirSign;
            float up = a.knockback_y;
            _fighter.ApplyImpulse(new Vector2(dirSign * power, up));
            _fighter.TriggerDashDust(dirSign);
        }

        void DoTeleport(SkillAction a)
        {
            var bm = PromptFighters.Battle.BattleManager.Instance;

            // behind_enemy: 相手の背後（自分から見て向こう側）へ回り込む。忍者・暗殺者の奇襲用。
            if (a.direction == "behind_enemy" && _fighter.Opponent != null)
            {
                var op = _fighter.Opponent.transform.position;
                float side = Mathf.Sign(op.x - _fighter.transform.position.x);
                if (side == 0f) side = _fighter.FacingRight ? 1f : -1f;
                Vector3 behind = new Vector3(op.x + side * 0.95f, _fighter.transform.position.y, 0f);
                if (bm != null)
                    behind.x = Mathf.Clamp(behind.x, bm.StageMinX + 0.5f, bm.StageMaxX - 0.5f);
                _fighter.transform.position = behind;
                PromptFighters.Audio.GameAudioManager.Instance?.PlayTeleport();
                return;
            }

            float dirSign = _fighter.FacingRight ? 1f : -1f;
            float distance = Mathf.Clamp(a.power > 0f ? a.power : 2.2f, 0.5f, 4f);
            Vector2 moveDirection;
            if (!string.IsNullOrEmpty(a.aim_mode) || !Mathf.Approximately(a.rotation_angle, 0f))
                moveDirection = ResolveAimDirection(a, _fighter.transform.position, new Vector2(dirSign, 0f));
            else
            {
                if (a.direction == "backward") dirSign = -dirSign;
                moveDirection = new Vector2(dirSign, 0f);
            }
            if (a.direction == "backward" && (!string.IsNullOrEmpty(a.aim_mode) || !Mathf.Approximately(a.rotation_angle, 0f)))
                moveDirection = -moveDirection;
            Vector3 pos = _fighter.transform.position + (Vector3)(moveDirection * distance);
            if (bm != null)
                pos.x = Mathf.Clamp(pos.x, bm.StageMinX + 0.5f, bm.StageMaxX - 0.5f);
            _fighter.transform.position = pos;
            PromptFighters.Audio.GameAudioManager.Instance?.PlayTeleport();
        }

        void DoJumpAttack(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float lift = a.power > 0f ? a.power : 5f;
            if (!string.IsNullOrEmpty(a.aim_mode) || !Mathf.Approximately(a.rotation_angle, 0f))
                _fighter.ApplyImpulse(ResolveAimDirection(a, _fighter.transform.position, Vector2.up) * lift);
            else
                _fighter.ApplyImpulse(new Vector2(0f, lift));
            SpawnAreaHitbox(skill, a, powerMultiplier);
        }

        // uppercut: 昇竜系の対空技。上昇しながら体に追従する判定で相手を巻き込み、打ち上げる。
        void DoUppercut(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            float lift = Mathf.Clamp(a.power > 0f ? a.power : 9f, 5f, 13f);
            _fighter.ApplyImpulse(new Vector2(dirSign * lift * 0.22f, lift), 0.28f);
            if (string.IsNullOrEmpty(a.knockback_direction)) a.knockback_direction = "up";
            if (a.duration <= 0f) a.duration = 0.38f; // 上昇中ずっと巻き込む
            if (a.size_x   <= 0f) a.size_x   = 1.4f;
            if (a.size_y   <= 0f) a.size_y   = 2.4f;
            if (a.spawn_x  <= 0f) a.spawn_x  = 0.45f;
            SpawnBodyHitbox(skill, a, powerMultiplier, swingFx: false);

            // 昇竜の立ち上るストリーク＋踏み込みの土煙
            Color ec = SkillEnumParser.ElementColor(skill.element);
            Battle.SimpleFX.RisingStreak(
                _fighter.transform.position + new Vector3(dirSign * 0.35f * _sizeScale, 0.3f, 0f),
                ec, 2.6f * _sizeScale);
            Battle.SimpleFX.Dust(_fighter.transform.position, 2, 1.1f);

            // 生成済みのエフェクト画像（縦向きの昇り演出）があれば上昇経路に重ねて表示する
            var fx = a.hide_effect ? null : _fighter.GetEffectSprite(skill);
            if (fx != null)
            {
                var go = new GameObject("UppercutFx");
                go.transform.position = _fighter.transform.position
                    + new Vector3(dirSign * 0.3f * _sizeScale, 1.4f * _sizeScale, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = fx;
                sr.flipX = !_fighter.FacingRight;
                sr.sortingOrder = 11;
                Vector2 ss = fx.bounds.size;
                float h = 3.0f * _sizeScale;
                float w = Mathf.Min(h * (ss.x / Mathf.Max(0.01f, ss.y)), 2.2f * _sizeScale);
                go.transform.localScale = new Vector3(
                    w / Mathf.Max(0.01f, ss.x), h / Mathf.Max(0.01f, ss.y), 1f);
                StartCoroutine(FadeAndDestroy(go, sr, 0.45f));
            }
        }

        // dive_attack: 急降下攻撃。斜め下へ突っ込み、着地時に左右へ小さな衝撃波を出す。
        // 地上発動時は小さく跳んでから急降下する（地上でも技として成立させる）。
        void DoDiveAttack(SkillData skill, SkillAction a, float powerMultiplier)
        {
            StartCoroutine(DiveRoutine(skill, a, powerMultiplier));
        }

        IEnumerator DiveRoutine(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            float power = Mathf.Clamp(a.power > 0f ? a.power : 10f, 6f, 15f);

            if (_fighter.IsGrounded)
            {
                _fighter.ApplyImpulse(new Vector2(dirSign * 1.5f, 8.5f), 0.15f);
                yield return new WaitForSeconds(0.24f);
                if (_fighter.State == FighterState.Dead) yield break;
            }

            _fighter.ApplyImpulse(new Vector2(dirSign * power * 0.45f, -power), 0.3f);

            // 降下中の巻き込み判定（体に追従）
            if (string.IsNullOrEmpty(a.knockback_direction)) a.knockback_direction = "diagonal_up";
            if (a.duration <= 0f) a.duration = 0.45f;
            if (a.size_x   <= 0f) a.size_x   = 1.5f;
            if (a.size_y   <= 0f) a.size_y   = 1.8f;
            SpawnBodyHitbox(skill, a, powerMultiplier, swingFx: false);

            // 着地したら左右に衝撃波（最大1秒待つ。落ち続けた場合は出さない）
            float t = 0f;
            while (!_fighter.IsGrounded && t < 1.0f) { t += Time.deltaTime; yield return null; }
            if (_fighter.IsGrounded && _fighter.State != FighterState.Dead)
            {
                SpawnGroundWave(skill, a, powerMultiplier * 0.8f, +1f, 1.4f, 1.6f, 0.7f, 0.3f, 0.18f);
                SpawnGroundWave(skill, a, powerMultiplier * 0.8f, -1f, 1.4f, 1.6f, 0.7f, 0.3f, 0.18f);
                // 着地インパクト演出（衝撃波リング＋土煙＋画面揺れ）
                Battle.SimpleFX.Shockwave(_fighter.transform.position, 1.3f * _sizeScale);
                Battle.SimpleFX.Dust(_fighter.transform.position, 3, 1.2f);
                Battle.CameraShake.Shake(0.2f, 0.22f);
            }
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

        // 自己バフのデータ駆動テーブル。status文字列→適用処理(Fighter, 倍率, 持続)。
        // 新しい自己バフはここに1行追加すればよく、BuffSelf本体を編集する必要がない。
        // 未登録のstatusはデフォルト(ダメージブースト)にフォールバックする。
        static readonly Dictionary<string, Action<Fighter, float, float>> s_selfBuffs =
            new Dictionary<string, Action<Fighter, float, float>>
        {
            ["speed"]       = (f, m, d) => f.StartTemporarySpeedChange(Mathf.Clamp(m, 1f, 1.7f), d),
            ["jump"]        = (f, m, d) => f.StartTemporaryJumpChange(Mathf.Clamp(m, 1f, 1.5f), d),
            ["invincible"]  = (f, m, d) => f.StartTemporaryInvincible(Mathf.Min(d, 1.2f)),
            ["transparent"] = (f, m, d) => f.StartTemporaryInvincible(Mathf.Min(d, 1.2f)),
            ["reflect"]     = (f, m, d) => f.StartTemporaryReflect(Mathf.Min(d, 3f)),
        };

        void BuffSelf(SkillAction a)
        {
            // 技発動で付与される自己バフは最低2秒は持続させる（無敵/透明は短い判定窓を維持するため対象外。
            // s_selfBuffs側でMathf.Min(d,1.2f)により1.2秒以下に固定されるので、ここで底上げしても無敵には影響しない）。
            float duration = Mathf.Max(2f, a.duration);
            float multiplier = a.power > 0f ? a.power : 1.2f;
            if (a.status != null && s_selfBuffs.TryGetValue(a.status, out var apply))
                apply(_fighter, multiplier, duration);
            else
                _fighter.StartTemporaryDamageBoost(Mathf.Clamp(multiplier, 1f, 1.6f), duration);
        }

        void DoReflector(SkillAction a)
        {
            // 技バフは最低2秒（無敵以外）。反射も2〜3秒を保証する。
            float duration = Mathf.Clamp(a.duration > 0f ? a.duration : 2f, 2f, 3f);
            _fighter.StartTemporaryReflect(duration);
        }

        void DoCounter(SkillAction a)
        {
            // 技バフは最低2秒（無敵以外）。カウンター構えも2〜3秒を保証する。
            float duration  = Mathf.Clamp(a.duration > 0f ? a.duration : 2f, 2f, 3f);
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
            Vector2 pos    = HasExplicitSpatialOrigin(a)
                ? ResolveSpatialOrigin(skill, a)
                : (Vector2)_fighter.transform.position + new Vector2(dirSign * spawnX * _sizeScale, spawnY * _sizeScale);
            // 壁際で使ってもステージ外に生まれないようにクランプ
            var bmSummon = PromptFighters.Battle.BattleManager.Instance;
            if (bmSummon != null)
                pos.x = Mathf.Clamp(pos.x, bmSummon.StageMinX + 0.6f, bmSummon.StageMaxX - 0.6f);
            float lifetime = a.duration > 0f ? a.duration : 3f;
            float speed    = a.power > 0f ? a.power : 2.5f;
            float dmg      = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage * 0.5f) * powerMultiplier;
            float kb       = skill.parameters.knockback * 0.6f * powerMultiplier;
            Vector2 authoredSize = a.size_x > 0f || a.size_y > 0f
                ? new Vector2(a.size_x > 0f ? a.size_x : 1.3f, a.size_y > 0f ? a.size_y : 1.7f)
                : EstimateSummonSize(skill);
            Vector2 desiredSize = authoredSize * _sizeScale;
            Vector2 baseMoveDirection = ResolveSummonMoveDirection(a, pos, dirSign);
            int requestedCount = a.pattern_count > 0 ? a.pattern_count : 1;
            bool hasSummonPattern = !string.IsNullOrEmpty(a.pattern) && a.pattern != "single";
            var samples = hasSummonPattern || requestedCount > 1
                ? BuildSpatialSamples(a, pos, baseMoveDirection, projectile: false, maxCount: 6)
                : new List<SpatialSample> { new SpatialSample(pos, baseMoveDirection, 0f) };
            float perSummonDamage = dmg / Mathf.Max(1, samples.Count);
            foreach (var sample in samples)
            {
                Vector2 summonPos = sample.position;
                void SpawnNow()
                {
                    ShowImpactAtSpawn(skill);
                    SummonEntity.Spawn(_fighter, summonPos, speed, lifetime, perSummonDamage, kb, skill.element,
                        a.hide_effect ? null : _fighter.GetEffectSprite(skill), desiredSize, a,
                        sample.direction, SummonUsesTrajectory(a));
                }
                bool remote = HasExplicitSpatialOrigin(a) && !string.IsNullOrEmpty(a.spawn_origin) && a.spawn_origin != "owner";
                if (remote || a.telegraph_time > 0f)
                    StartCoroutine(TelegraphThenSpawn(summonPos, desiredSize, sample.Angle, "box", skill.element,
                        a.telegraph_time > 0f ? Mathf.Clamp(a.telegraph_time, 0.05f, 1.5f) : 0.4f, SpawnNow));
                else
                    SpawnNow();
            }
        }

        Vector2 ResolveSummonMoveDirection(SkillAction a, Vector2 from, float facing)
        {
            if (!string.IsNullOrEmpty(a.aim_mode) || !Mathf.Approximately(a.rotation_angle, 0f))
                return ResolveAimDirection(a, from, new Vector2(facing, 0f));
            return a.direction switch
            {
                "up" or "upward"               => Vector2.up,
                "down" or "downward"           => Vector2.down,
                "diagonal_up"                   => new Vector2(facing, 0.7f).normalized,
                "diagonal_down"                 => new Vector2(facing, -0.7f).normalized,
                "left"                          => Vector2.left,
                "right"                         => Vector2.right,
                "backward"                      => new Vector2(-facing, 0f),
                "toward_enemy"                  => ResolveAimDirectionWithMode(a, from, new Vector2(facing, 0f), "enemy"),
                "away_enemy"                    => ResolveAimDirectionWithMode(a, from, new Vector2(-facing, 0f), "away_enemy"),
                _                                => new Vector2(facing, 0f),
            };
        }

        Vector2 ResolveAimDirectionWithMode(SkillAction a, Vector2 from, Vector2 fallback, string mode)
        {
            string original = a.aim_mode;
            a.aim_mode = mode;
            Vector2 result = ResolveAimDirection(a, from, fallback);
            a.aim_mode = original;
            return result;
        }

        static bool SummonUsesTrajectory(SkillAction a)
        {
            if (a == null) return false;
            if (a.homing || a.player_controlled || a.direction == "stationary" || a.direction == "hover") return false;
            return !string.IsNullOrEmpty(a.aim_mode) || HasNewPattern(a) || a.boomerang || a.orbit ||
                   a.wave_amplitude > 0f || a.gravity_scale > 0f ||
                   a.direction == "up" || a.direction == "upward" ||
                   a.direction == "down" || a.direction == "downward" ||
                   a.direction == "diagonal_up" || a.direction == "diagonal_down" ||
                   a.direction == "left" || a.direction == "right" ||
                   a.direction == "backward" || a.direction == "toward_enemy" || a.direction == "away_enemy";
        }

        // 旧JSONなどでsize_x/yが無い場合も、召喚物の名称・説明から体格を決める。
        // 新規生成はAIにsize_x/yを明示させるため、ここは後方互換の安全な補完経路。
        static Vector2 EstimateSummonSize(SkillData skill)
        {
            string text = ((skill?.skill_name ?? "") + " " + (skill?.description ?? "")).ToLowerInvariant();
            bool large = text.Contains("巨大") || text.Contains("超大型") || text.Contains("大型") ||
                         text.Contains("ドラゴン") || text.Contains("竜") || text.Contains("ゴーレム") ||
                         text.Contains("戦車") || text.Contains("巨人") || text.Contains("大型獣");
            if (large) return new Vector2(2.8f, 3.6f);
            bool small = text.Contains("小型") || text.Contains("ミニ") || text.Contains("妖精") ||
                         text.Contains("ドローン") || text.Contains("子") || text.Contains("使い魔");
            if (small) return new Vector2(0.85f, 1.15f);
            bool longBody = text.Contains("蛇") || text.Contains("ヘビ") || text.Contains("列車") || text.Contains("隊列");
            if (longBody) return new Vector2(2.6f, 1.25f);
            return new Vector2(1.3f, 1.7f);
        }

        // wall: 通行を遮る、時間・耐久で消える設置壁。攻撃判定を持たないため理不尽な即時ダメージは発生しない。
        void SpawnWall(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            float width = (a.size_x > 0f ? a.size_x : 1.6f) * _sizeScale;
            float height = (a.size_y > 0f ? a.size_y : 2.5f) * _sizeScale;
            float offsetX = a.spawn_x > 0f ? a.spawn_x : 1.35f;
            Vector2 pos = HasExplicitSpatialOrigin(a)
                ? ResolveSpatialOrigin(skill, a)
                : (Vector2)_fighter.transform.position + new Vector2(dirSign * offsetX * _sizeScale, 0f);
            var bm = BattleManager.Instance;
            float groundY = bm != null ? bm.StageGroundY : pos.y - height * 0.5f;
            pos.y = groundY + height * 0.5f;
            if (bm != null)
                pos.x = Mathf.Clamp(pos.x, bm.StageMinX + width * 0.5f + 0.15f, bm.StageMaxX - width * 0.5f - 0.15f);
            float lifetime = a.duration > 0f ? a.duration : 3f;
            float durability = a.power > 0f ? a.power : 20f;
            Vector2 size = new Vector2(width, height);
            void SpawnNow()
            {
                ShowImpactAtSpawn(skill);
                SummonEntity.SpawnWall(_fighter, pos, lifetime, durability,
                    a.hide_effect ? null : _fighter.GetEffectSprite(skill), size);
            }
            bool remote = HasExplicitSpatialOrigin(a) && !string.IsNullOrEmpty(a.spawn_origin) && a.spawn_origin != "owner";
            if (remote || a.telegraph_time > 0f)
                StartCoroutine(TelegraphThenSpawn(pos, size, 0f, "box", skill.element,
                    a.telegraph_time > 0f ? Mathf.Clamp(a.telegraph_time, 0.2f, 1.5f) : 0.45f, SpawnNow));
            else SpawnNow();
        }

        // heal_self: HP回復。power=回復量(HP)。未指定なら最大HPの5%。
        void HealSelf(SkillAction a)
        {
            float amount = a.power > 0f ? a.power : _fighter.MaxHP * 0.05f;
            _fighter.Heal(amount);
        }

        // barrier: 次に受ける1技を完全無効化する。powerは互換用に受け取るが耐久値には使わない。
        // コルーチンで張るだけなので発動者はすぐ動ける（後隙は技のrecoveryで制御）。
        void DoBarrier(SkillAction a)
        {
            float duration = a.duration > 0f ? a.duration : 3f;
            _fighter.StartBarrier(1f, duration);
        }

        // command_throw: 範囲内の相手を掴み→引き寄せ→ガード不能の投げで締める（ワイヤー投げ）。
        void DoCommandThrow(SkillData skill, SkillAction a, float powerMultiplier)
        {
            if (_fighter.Opponent == null) return;

            // rangeを伸ばせばワイヤー投げ（遠距離から掴んで引き寄せる）になる。
            float range  = (a.range > 0f ? a.range
                          : (skill.parameters.range > 0f ? skill.parameters.range : 1.6f)) * _sizeScale;
            float height = (a.size_y > 0f ? a.size_y : 2.0f) * _sizeScale;
            float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage)
                        * powerMultiplier * _fighter.EffectiveDamageMultiplier;
            var (kbDir, _) = ComputeKnockback(a, 1f, 0.8f);
            Vector2 throwDir = new Vector2(Mathf.Abs(kbDir.x), Mathf.Abs(kbDir.y));
            _fighter.StartCommandThrow(range, height, dmg,
                skill.parameters.knockback * powerMultiplier, throwDir, skill.parameters.stun_time);
        }

        // shockwave: 地面叩きつけで左右に発生する衝撃波。effect spriteで判定と見た目を一致させる。
        void SpawnShockwave(SkillData skill, SkillAction a, float powerMultiplier)
        {
            float width   = (a.size_x > 0f ? a.size_x : 2.0f);
            float height  = (a.size_y > 0f ? a.size_y : 0.8f);
            float dist    = (a.range  > 0f ? a.range  : 2.2f) * _sizeScale; // 中心から左右へのオフセット
            float groundY = (!Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 0.3f) * _sizeScale;
            float life    = skill.parameters.active_time > 0f ? skill.parameters.active_time : 0.25f;
            SpawnGroundWave(skill, a, powerMultiplier, +1f, dist, width, height, groundY, life);
            SpawnGroundWave(skill, a, powerMultiplier, -1f, dist, width, height, groundY, life);
            // 地面叩きつけの手応え（土煙＋軽い画面揺れ）
            Battle.SimpleFX.Dust(_fighter.transform.position, 2, 1.1f);
            Battle.CameraShake.Shake(0.12f, 0.15f);
        }

        void SpawnGroundWave(SkillData skill, SkillAction a, float powerMultiplier,
                             float side, float dist, float width, float height, float groundY, float life)
        {
            float w = width  * _sizeScale;
            float h = height * _sizeScale;
            Vector2 pos = (Vector2)_fighter.transform.position + new Vector2(side * dist, groundY);
            var hb = Hitbox.Spawn(_fighter, pos,
                new Vector2(w * HitboxVisualScale, h * HitboxVisualScale), life);
            float dmg = (a.damage_override >= 0f ? a.damage_override : skill.parameters.damage)
                        * powerMultiplier * _fighter.EffectiveDamageMultiplier;
            hb.Damage         = dmg;
            hb.DamageIncludesOwnerBoost = true;
            hb.Knockback      = skill.parameters.knockback * powerMultiplier;
            var (kbDir, kbFixed) = ComputeKnockback(a, 0.6f, 1.0f);
            hb.KnockbackDir      = kbDir;
            hb.FixedKnockbackDir = kbFixed;
            hb.GroundBounce      = a.knockback_direction == "ground_bounce";
            hb.StunTime       = skill.parameters.stun_time;
            hb.GuardDamage    = skill.parameters.guard_damage;
            hb.Element        = skill.element;
            // 技のエフェクト画像が無ければ組み込みの衝撃波スプライトで見せる
            // （グロー玉フォールバックでは「衝撃波」に見えないため）。
            Sprite waveFx = a.hide_effect ? null : _fighter.GetEffectSprite(skill);
            if (waveFx == null && !a.hide_effect) waveFx = Battle.SimpleFX.GetSprite("shockwave");
            hb.EffectSprite   = waveFx;
            hb.HideVisual     = a.hide_effect;
            hb.FlipEffectX    = side < 0f;
            hb.MaxHits        = a.hit_count > 0 ? a.hit_count : skill.parameters.hit_count;
            ApplyActionStatus(hb, a);
        }

        // gravity_well: 一定時間、相手を一点へ継続引き寄せ。引き寄せ半径＝表示ビジュアル径で一致させる。
        void DoGravityWell(SkillData skill, SkillAction a)
        {
            float dirSign = _fighter.FacingRight ? 1f : -1f;
            float spawnX  = !Mathf.Approximately(a.spawn_x, 0f) ? a.spawn_x : 2.5f;
            float spawnY  = !Mathf.Approximately(a.spawn_y, 0f) ? a.spawn_y : 1.0f;
            Vector2 center = HasExplicitSpatialOrigin(a)
                ? ResolveSpatialOrigin(skill, a)
                : (Vector2)_fighter.transform.position
                  + new Vector2(dirSign * spawnX * _sizeScale, spawnY * _sizeScale);
            float radius   = (a.range > 0f ? a.range : 3.5f) * _sizeScale;
            // 引き寄せ力を弱め、拘束時間も短くする（拘束される側が振り切って動けるように）。
            float force    = Mathf.Clamp(a.power > 0f ? a.power : 12f, 3f, 18f);
            float duration = Mathf.Min(a.duration > 0f ? a.duration : 0.7f, 0.9f);
            _fighter.StartGravityWell(center, radius, force, duration);
            if (!a.hide_effect)
                SpawnFieldVisual(center, radius * 2f, _fighter.GetEffectSprite(skill), skill.element, duration);
        }

        // 引き寄せ範囲などの「効果範囲」を可視化する非接触ビジュアル。直径＝効果範囲で一致させる。
        void SpawnFieldVisual(Vector2 center, float diameter, Sprite sprite, Element element, float duration)
        {
            var go = new GameObject("FieldVisual");
            go.transform.position = center;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite != null ? sprite : RuntimeSprite.FallbackField();
            sr.sortingOrder = 9;
            Color c = SkillEnumParser.ElementColor(element);
            sr.color = new Color(c.r, c.g, c.b, 0.5f);
            Vector2 ss = sr.sprite.bounds.size;
            if (ss.x > 0f && ss.y > 0f)
                go.transform.localScale = new Vector3(diameter / ss.x, diameter / ss.y, 1f);
            StartCoroutine(FadeAndDestroy(go, sr, duration));
        }

        IEnumerator FadeAndDestroy(GameObject go, SpriteRenderer sr, float duration)
        {
            float t = 0f;
            Color baseC = sr.color;
            while (t < duration && go != null)
            {
                t += Time.deltaTime;
                if (sr != null)
                    sr.color = new Color(baseC.r, baseC.g, baseC.b, Mathf.Lerp(baseC.a, 0f, t / duration));
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        // apply_status は相手に状態異常を付与する。近距離内でchance判定あり。
        void ApplyOpponentStatus(SkillAction a)
        {
            if (_fighter.Opponent == null) return;
            var st = SkillEnumParser.ParseStatus(a.status);
            if (st == StatusType.None) return;
            if (UnityEngine.Random.value > a.chance) return;
            _fighter.Opponent.ApplyStatus(st, a.status_duration > 0f ? a.status_duration : a.duration);
        }

        (Vector2 kbDir, bool isFixed) ComputeKnockback(SkillAction a, float defaultX, float defaultY)
        {
            float facingSign = _fighter != null && _fighter.FacingRight ? 1f : -1f;
            float rawX = !Mathf.Approximately(a.knockback_x, 0f) ? a.knockback_x : defaultX;
            float rawY = !Mathf.Approximately(a.knockback_y, 0f) ? a.knockback_y : defaultY;
            float x = Mathf.Abs(rawX);
            float y = Mathf.Abs(rawY);
            return (string.IsNullOrEmpty(a.knockback_direction) ? "away" : a.knockback_direction) switch
            {
                "up"           => (new Vector2(0f,                  1.5f), true),
                "spike"        => (new Vector2(facingSign * 0.15f, -1.2f), true),
                "toward"       => (new Vector2(-facingSign * x,       y  ), true),
                "diagonal_up"  => (new Vector2(facingSign * 0.4f,   1.2f), true),
                "ground_bounce"=> (new Vector2(facingSign * 0.25f, -1.4f), true),
                "vector"       => (new Vector2(facingSign * rawX, rawY), true),
                "along_attack" => (new Vector2(facingSign, 0f), true),
                "along"        => (new Vector2(facingSign, 0f), true),
                "from_origin"  => (new Vector2(facingSign * x, y), true),
                "from"         => (new Vector2(facingSign * x, y), true),
                "toward_origin"=> (new Vector2(-facingSign * x, y), true),
                _              => (new Vector2(x, y),                      false),
            };
        }

        static void ApplyActionStatus(Hitbox hb, SkillAction a)
        {
            var st = SkillEnumParser.ParseStatus(a.status);
            if (st == StatusType.None) return;
            hb.Status = st;
            hb.StatusDuration = a.status_duration > 0f ? a.status_duration : a.duration;
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
