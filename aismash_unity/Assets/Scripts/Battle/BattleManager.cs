using UnityEngine;

namespace PromptFighters.Battle
{
    public enum BattleState { WaitingToStart, Fighting, Ended }

    public class BattleManager : MonoBehaviour
    {
        public static BattleManager Instance { get; private set; }

        [Header("Settings")]
        public float battleDuration = 60f;

        [Header("References")]
        public Fighter fighter1;
        public Fighter fighter2;
        public Vector3 fighter1SpawnPos = new Vector3(-4f, 1f, 0f);
        public Vector3 fighter2SpawnPos = new Vector3( 4f, 1f, 0f);

        public float       TimeRemaining { get; private set; }
        public BattleState State         { get; private set; } = BattleState.WaitingToStart;

        public event System.Action<float> OnTimerChanged;
        public event System.Action<int>   OnBattleEnd; // 0=1P wins, 1=2P wins, -1=draw

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            if (fighter1 != null) fighter1.OnDeath += () => EndBattle(1);
            if (fighter2 != null) fighter2.OnDeath += () => EndBattle(0);
            StartBattle();
        }

        void Update()
        {
            if (State != BattleState.Fighting) return;

            TimeRemaining = Mathf.Max(0f, TimeRemaining - Time.deltaTime);
            OnTimerChanged?.Invoke(TimeRemaining);

            if (TimeRemaining <= 0f) EndByTimeout();
        }

        public void StartBattle()
        {
            TimeRemaining = battleDuration;
            State         = BattleState.Fighting;
            fighter1?.ResetForBattle(fighter1SpawnPos, faceRight: true);
            fighter2?.ResetForBattle(fighter2SpawnPos, faceRight: false);
        }

        void EndByTimeout()
        {
            if (fighter1 == null || fighter2 == null) { EndBattle(-1); return; }
            if      (fighter1.CurrentHP > fighter2.CurrentHP) EndBattle(0);
            else if (fighter2.CurrentHP > fighter1.CurrentHP) EndBattle(1);
            else                                               EndBattle(-1);
        }

        void EndBattle(int winnerIndex)
        {
            if (State == BattleState.Ended) return;
            State = BattleState.Ended;
            OnBattleEnd?.Invoke(winnerIndex);
            Debug.Log($"[Battle] {(winnerIndex < 0 ? "Draw" : $"{winnerIndex + 1}P Wins")}");
        }
    }
}
