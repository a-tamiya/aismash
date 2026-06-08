using UnityEngine;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // CPU操作。FighterInput の代わりに Fighter / SkillExecutor の公開APIを叩くだけの簡易AI。
    // OpenAI API には一切依存しない。判断は一定間隔で行い、反応遅延と確率で「弱め・自然」に寄せる。
    [RequireComponent(typeof(Fighter))]
    public class FighterAI : MonoBehaviour
    {
        public enum CpuLevel { Off, Easy, Normal, Hard }

        // ロビーの「CPU対戦」トグル。BattleManager が 2P 側へ適用する。
        public static CpuLevel Level = CpuLevel.Off;
        public static bool Enabled => Level != CpuLevel.Off;

        [Range(0f, 1f)] public float aggression       = 0.6f;  // 攻めの頻度
        [Range(0f, 1f)] public float defense          = 0.5f;  // ガード/回避の頻度
        public float decisionInterval = 0.18f;                 // 判断間隔（秒）。小さいほど反応が速い＝強い

        const float AttackRange = 1.5f;

        Fighter _fighter;
        SkillExecutor _skills;
        float _decisionTimer;
        float _moveDir;
        bool  _guardHold;
        float _guardReleaseTimer;
        float _actionCooldown;

        void Awake()
        {
            _fighter = GetComponent<Fighter>();
            _skills  = GetComponent<SkillExecutor>();
        }

        void OnEnable()
        {
            ApplyLevel();
        }

        // ロビートグルの難易度を反映。
        public void ApplyLevel() => ApplyLevel(Level);

        // 難易度プリセットを反映。攻め頻度・守り頻度・反応速度（判断間隔）で強弱を表現。
        // ボス等、グローバル設定と独立に強さを固定したい場合は level を明示指定する。
        public void ApplyLevel(CpuLevel level)
        {
            switch (level)
            {
                case CpuLevel.Easy:
                    aggression = 0.35f; defense = 0.30f; decisionInterval = 0.30f; break;
                case CpuLevel.Hard:
                    aggression = 0.85f; defense = 0.75f; decisionInterval = 0.10f; break;
                default: // Normal
                    aggression = 0.60f; defense = 0.50f; decisionInterval = 0.18f; break;
            }
        }

        void Update()
        {
            if (_fighter.State == FighterState.Dead) { _fighter.Move(0f); return; }

            var bm = BattleManager.Instance;
            if (bm != null && !bm.IsFighting)
            {
                _fighter.Move(0f);
                _fighter.SetGuard(false);
                return;
            }

            var opp = _fighter.Opponent;
            if (opp == null || opp.State == FighterState.Dead)
            {
                _fighter.Move(0f);
                _fighter.SetGuard(false);
                return;
            }

            // 掴み中は即投げ／掴まれ中は何もしない
            if (_fighter.IsHoldingOpponent) { _fighter.ThrowHeld(_fighter.FacingRight); return; }
            if (_fighter.IsGrabbed) return;

            if (_actionCooldown > 0f) _actionCooldown -= Time.deltaTime;
            if (_guardReleaseTimer > 0f)
            {
                _guardReleaseTimer -= Time.deltaTime;
                if (_guardReleaseTimer <= 0f) _guardHold = false;
            }

            _decisionTimer -= Time.deltaTime;
            if (_decisionTimer <= 0f)
            {
                _decisionTimer = decisionInterval * Random.Range(0.7f, 1.3f);
                Decide(opp);
            }

            // 毎フレーム反映（移動・ガード維持）
            _fighter.Move(_guardHold ? 0f : _moveDir);
            _fighter.SetGuard(_guardHold && _fighter.IsGrounded);
        }

        void Decide(Fighter opp)
        {
            float dx   = opp.transform.position.x - transform.position.x;
            float dy   = opp.transform.position.y - transform.position.y;
            float dist = Mathf.Abs(dx);
            int   sign = dx >= 0f ? 1 : -1;

            // 相手が技モーション中で近いなら、守りを選択
            bool threat = dist < 2.4f && OpponentAttacking(opp);
            if (threat && Random.value < defense)
            {
                if (Random.value < 0.4f && _fighter.TryDodge(new Vector2(-sign, 0f)))
                {
                    _guardHold = false;
                    _moveDir   = 0f;
                    return;
                }
                _guardHold         = true;
                _guardReleaseTimer = Random.Range(0.25f, 0.5f);
                _moveDir           = 0f;
                return;
            }

            _guardHold = false;

            if (dist > AttackRange + 0.3f)
            {
                // 接近
                _moveDir = sign;
                if (_fighter.IsGrounded && (dy > 1.2f || Random.value < 0.05f))
                    _fighter.Jump();
                // 遠距離なら飛び道具で牽制
                if (dist > 4f && _actionCooldown <= 0f && Random.value < aggression * 0.5f)
                    Attack(sign, preferRanged: true);
            }
            else
            {
                // 間合い内
                _moveDir = 0f;
                if (_actionCooldown <= 0f && Random.value < aggression)
                {
                    if (dist < 1.0f && Random.value < 0.2f && _fighter.TryStartGrab())
                    {
                        _actionCooldown = Random.Range(0.4f, 0.8f);
                        return;
                    }
                    Attack(sign, preferRanged: false);
                }
                else if (Random.value < 0.3f)
                {
                    _moveDir = Random.value < 0.5f ? sign : -sign; // 軽く間合い調整
                }
            }
        }

        void Attack(int sign, bool preferRanged)
        {
            if (_skills == null || _skills.IsExecuting || !_fighter.CanAct) return;
            _fighter.FaceTowardInput(sign);

            SkillSlot slot;
            if (preferRanged && TryFindRangedSlot(out var ranged))
                slot = ranged;
            else
                slot = PickSlot();

            if (_skills.TryUseSkill(slot))
                _actionCooldown = Random.Range(0.35f, 0.7f);
        }

        SkillSlot PickSlot()
        {
            float r = Random.value;
            if (r < 0.4f) return SkillSlot.AttackA;
            if (r < 0.7f) return SkillSlot.AttackB;
            if (r < 0.9f) return SkillSlot.AttackC;
            return SkillSlot.SmashSide;
        }

        bool TryFindRangedSlot(out SkillSlot slot)
        {
            for (int i = 0; i < 4; i++)
            {
                var s = _skills.GetSkill((SkillSlot)i);
                if (s?.actions == null) continue;
                foreach (var a in s.actions)
                    if (a != null && (a.type == "projectile" || a.type == "beam"))
                    {
                        slot = (SkillSlot)i;
                        return true;
                    }
            }
            slot = SkillSlot.AttackB;
            return false;
        }

        static bool OpponentAttacking(Fighter opp)
        {
            var ex = opp.GetComponent<SkillExecutor>();
            return ex != null && ex.IsExecuting;
        }
    }
}
