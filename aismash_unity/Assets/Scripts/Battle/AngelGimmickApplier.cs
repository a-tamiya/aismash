using UnityEngine;
using PromptFighters.AI;
using PromptFighters.Audio;

namespace PromptFighters.Battle
{
    // 天使ギミックを Fighter に適用する
    public class AngelGimmickApplier : MonoBehaviour
    {
        public void Apply(GimmickData data, Fighter p1, Fighter p2)
        {
            if (data == null) return;

            ApplySingle(data.gimmick, data.target, value, duration, p1, p2);

            if (!string.IsNullOrEmpty(data.gimmick2))
                ApplySingle(data.gimmick2, data.target2, value2, duration2, p1, p2);
        }

        void ApplySingle(string gimmick, string targetKey, float value, float duration, Fighter p1, Fighter p2)
        {
            Fighter target1 = ResolveTarget(targetKey, p1, p2, primary: true);
            Fighter target2 = targetKey == "both"
                ? ResolveTarget(targetKey, p1, p2, primary: false)
                : null;

            switch (gimmick)
            {
                case "hp_recover":
                    HealIfAlive(target1, value);
                    HealIfAlive(target2, value);
                    GameAudioManager.Instance?.PlayGimmickHeal();
                    break;
                case "speed_boost":
                    target1?.StartTemporarySpeedChange(value, duration);
                    target2?.StartTemporarySpeedChange(value, duration);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "speed_down":
                    target1?.StartTemporarySpeedChange(value, duration);
                    target2?.StartTemporarySpeedChange(value, duration);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "jump_boost":
                    target1?.StartTemporaryJumpChange(value, duration);
                    target2?.StartTemporaryJumpChange(value, duration);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "damage_boost":
                    target1?.StartTemporaryDamageBoost(value, duration);
                    target2?.StartTemporaryDamageBoost(value, duration);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "transparent":
                case "invincible":
                    target1?.StartTemporaryInvincible(duration);
                    target2?.StartTemporaryInvincible(duration);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "chaos":
                    target1?.StartTemporaryChaos(duration);
                    target2?.StartTemporaryChaos(duration);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
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
