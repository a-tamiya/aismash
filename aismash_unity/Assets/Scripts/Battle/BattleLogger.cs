using UnityEngine;
using PromptFighters.Battle.Skills;
using System.Collections.Generic;
using System.Text;

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
        readonly Queue<string> _recentEvents = new Queue<string>();

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
            _recentEvents.Clear();
        }

        // SkillExecutor から呼ぶ
        public void LogSkillUse(int playerIndex, SkillSlot slot, string skillName)
        {
            if (BattleManager.Instance == null || !BattleManager.Instance.IsFighting) return;
            if (BattleManager.Instance.IsTraining) return;
            var log = playerIndex == 0 ? P1 : P2;
            log.skillUseCounts[(int)slot]++;
            log.skillNames[(int)slot] = skillName;
            LogEvent($"{Label(playerIndex)}が「{skillName}」を使用");
        }

        // Fighter.OnDamageReceived から呼ぶ（receiver側 = ダメージを受けた方）
        public void LogDamage(int attackerPlayerIndex, float damage)
        {
            if (BattleManager.Instance == null || !BattleManager.Instance.IsFighting) return;
            if (BattleManager.Instance.IsTraining) return;
            var log = attackerPlayerIndex == 0 ? P1 : P2;
            log.totalDamageDealt += damage;
            var receiver = attackerPlayerIndex == 0 ? P2 : P1;
            receiver.totalDamageReceived += damage;
            if (damage >= 8f)
                LogEvent($"{Label(attackerPlayerIndex)}が{damage:0}ダメージの大技をヒット");
        }

        public void LogEvent(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (BattleManager.Instance != null &&
                (!BattleManager.Instance.IsFighting || BattleManager.Instance.IsTraining)) return;
            _recentEvents.Enqueue(text);
            while (_recentEvents.Count > 8) _recentEvents.Dequeue();
        }

        public string RecentEventsSummary()
        {
            if (_recentEvents.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var e in _recentEvents)
            {
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(e);
            }
            return sb.ToString();
        }

        static string Label(int playerIndex) => playerIndex == 0 ? "1P" : "2P";
    }
}
