using UnityEngine;
using PromptFighters.AI;

namespace PromptFighters.Battle
{
    // 天使ギミックを Fighter に適用する
    public class AngelGimmickApplier : MonoBehaviour
    {
        public void Apply(GimmickData data, Fighter p1, Fighter p2)
        {
            if (data == null) return;

            Fighter target1 = ResolveTarget(data.target, p1, p2, primary: true);
            Fighter target2 = data.target == "both"
                ? ResolveTarget(data.target, p1, p2, primary: false)
                : null;

            switch (data.gimmick)
            {
                case "hp_recover":
                    HealIfAlive(target1, data.value);
                    HealIfAlive(target2, data.value);
                    break;
                case "speed_boost":
                case "speed_down":
                    target1?.StartTemporarySpeedChange(data.value, data.duration);
                    target2?.StartTemporarySpeedChange(data.value, data.duration);
                    break;
                case "jump_boost":
                    target1?.StartTemporaryJumpChange(data.value, data.duration);
                    target2?.StartTemporaryJumpChange(data.value, data.duration);
                    break;
                case "damage_boost":
                    target1?.StartTemporaryDamageBoost(data.value, data.duration);
                    target2?.StartTemporaryDamageBoost(data.value, data.duration);
                    break;
                case "transparent":
                case "invincible":
                    target1?.StartTemporaryInvincible(data.duration);
                    target2?.StartTemporaryInvincible(data.duration);
                    break;
                case "chaos":
                    target1?.StartTemporaryChaos(data.duration);
                    target2?.StartTemporaryChaos(data.duration);
                    break;
            }
        }

        static void HealIfAlive(Fighter f, float ratio)
        {
            if (f == null || f.State == FighterState.Dead) return;
            f.HealHP(f.maxHP * Mathf.Clamp(ratio, 0f, 0.3f));
        }

        static Fighter ResolveTarget(string target, Fighter p1, Fighter p2, bool primary)
        {
            switch (target)
            {
                case "player1":  return primary ? p1 : null;
                case "player2":  return primary ? p2 : null;
                case "both":     return primary ? p1 : p2;
                case "weaker":
                    if (p1 == null) return primary ? p2 : null;
                    if (p2 == null) return primary ? p1 : null;
                    return primary ? (p1.CurrentHP <= p2.CurrentHP ? p1 : p2) : null;
                case "stronger":
                    if (p1 == null) return primary ? p2 : null;
                    if (p2 == null) return primary ? p1 : null;
                    return primary ? (p1.CurrentHP >= p2.CurrentHP ? p1 : p2) : null;
                default:
                    return primary ? p1 : p2;
            }
        }
    }
}
