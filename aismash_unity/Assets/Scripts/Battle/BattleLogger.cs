using UnityEngine;
using PromptFighters.Battle.Skills;
using System.Collections.Generic;
using System.Text;

namespace PromptFighters.Battle
{
    public class PlayerLog
    {
        public readonly int[]    skillUseCounts = new int[4];
        public readonly string[] skillNames     = new string[4];
        public float  totalDamageDealt;
        public float  totalDamageReceived;
        public int    hitStreak;
        public int    maxHitStreak;
        public string lastSkillName = "---";
        public int    guardBreaksDealt;

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
        readonly Queue<string> _recentEvents  = new Queue<string>();
        readonly Queue<float>  _p1RecentHits  = new Queue<float>();
        readonly Queue<float>  _p2RecentHits  = new Queue<float>();

        public int P1RecentHits => CountRecent(_p1RecentHits, 5f);
        public int P2RecentHits => CountRecent(_p2RecentHits, 5f);

        int CountRecent(Queue<float> q, float window)
        {
            float cutoff = Time.time - window;
            while (q.Count > 0 && q.Peek() < cutoff) q.Dequeue();
            return q.Count;
        }

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
            _p1RecentHits.Clear();
            _p2RecentHits.Clear();
        }

        // SkillExecutor から呼ぶ
        public void LogSkillUse(int playerIndex, SkillSlot slot, string skillName)
        {
            if (BattleManager.Instance == null || !BattleManager.Instance.IsFighting) return;
            if (BattleManager.Instance.IsTraining) return;
            var log = playerIndex == 0 ? P1 : P2;
            log.skillUseCounts[(int)slot]++;
            log.skillNames[(int)slot] = skillName;
            log.lastSkillName = skillName;
            LogEvent($"{Label(playerIndex)}が「{skillName}」を使用");
        }

        // Fighter.OnDamageReceived から呼ぶ（receiver側 = ダメージを受けた方）
        public void LogDamage(int attackerPlayerIndex, float damage)
        {
            if (BattleManager.Instance == null || !BattleManager.Instance.IsFighting) return;
            if (BattleManager.Instance.IsTraining) return;
            var attacker = attackerPlayerIndex == 0 ? P1 : P2;
            var receiver = attackerPlayerIndex == 0 ? P2 : P1;
            attacker.totalDamageDealt += damage;
            receiver.totalDamageReceived += damage;
            attacker.hitStreak++;
            attacker.maxHitStreak = Mathf.Max(attacker.maxHitStreak, attacker.hitStreak);
            receiver.hitStreak = 0;
            if (attackerPlayerIndex == 0) _p1RecentHits.Enqueue(Time.time);
            else                          _p2RecentHits.Enqueue(Time.time);
            if (damage >= 8f)
                LogEvent($"{Label(attackerPlayerIndex)}が{damage:0}ダメージの大技をヒット");
        }

        public void LogGuardBreak(int attackerIndex)
        {
            if (BattleManager.Instance == null || !BattleManager.Instance.IsFighting) return;
            var log = attackerIndex == 0 ? P1 : P2;
            log.guardBreaksDealt++;
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
