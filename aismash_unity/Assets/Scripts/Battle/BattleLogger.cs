using UnityEngine;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    public class PlayerLog
    {
        public readonly int[]   skillUseCounts = new int[4];
        public readonly string[] skillNames    = new string[4];
        public float totalDamageDealt;
        public float totalDamageReceived;

        public int MostUsedSlot()
        {
            int best = 0;
            for (int i = 1; i < 4; i++)
                if (skillUseCounts[i] > skillUseCounts[best]) best = i;
            return best;
        }

        public string MostUsedSkillName()
        {
            int s = MostUsedSlot();
            return skillUseCounts[s] > 0 ? skillNames[s] ?? "---" : "---";
        }
    }

    // バトル中の統計を収集する。BattleManagerと同じGameObjectに付ける。
    public class BattleLogger : MonoBehaviour
    {
        public static BattleLogger Instance { get; private set; }

        public PlayerLog P1 { get; private set; } = new PlayerLog();
        public PlayerLog P2 { get; private set; } = new PlayerLog();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void Start()
        {
            if (BattleManager.Instance != null)
                BattleManager.Instance.OnBattleStart += ResetLogs;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void ResetLogs()
        {
            P1 = new PlayerLog();
            P2 = new PlayerLog();
        }

        // SkillExecutor から呼ぶ
        public void LogSkillUse(int playerIndex, SkillSlot slot, string skillName)
        {
            if (BattleManager.Instance == null || !BattleManager.Instance.IsFighting) return;
            if (BattleManager.Instance.IsTraining) return;
            var log = playerIndex == 0 ? P1 : P2;
            log.skillUseCounts[(int)slot]++;
            log.skillNames[(int)slot] = skillName;
        }

        // Fighter.OnDamageReceived から呼ぶ（receiver側 = ダメージを受けた方）
        public void LogDamage(int attackerPlayerIndex, float damage)
        {
            if (BattleManager.Instance == null || !BattleManager.Instance.IsFighting) return;
            if (BattleManager.Instance.IsTraining) return;
            var log = attackerPlayerIndex == 0 ? P1 : P2;
            log.totalDamageDealt += damage;
        }
    }
}
