using UnityEngine;
using PromptFighters.Battle.Skills;
using PromptFighters.Utils;
using System.Collections;

namespace PromptFighters.Battle
{
    public enum BattlePhase
    {
        Setup,      // 名前入力・プリセット選択中
        Training,   // AI生成待ち・操作確認中
        Countdown,  // 3,2,1,FIGHT!
        Fighting,   // 対戦中
        Ended,      // 勝敗確定
    }

    public class BattleManager : MonoBehaviour
    {
        public static BattleManager Instance { get; private set; }

        [Header("Settings")]
        public float battleDuration  = 60f;
        public float countdownLength = 3f;
        public float trainingRespawnDelay = 1f;

        [Header("References")]
        public Fighter fighter1;
        public Fighter fighter2;
        public Vector3 fighter1SpawnPos = new Vector3(-4f, 1f, 0f);
        public Vector3 fighter2SpawnPos = new Vector3( 4f, 1f, 0f);

        public float       TimeRemaining { get; private set; }
        public BattlePhase Phase         { get; private set; } = BattlePhase.Setup;
        public float       Countdown     { get; private set; }

        // 旧 BattleState 互換プロパティ
        public bool IsFighting => Phase == BattlePhase.Fighting || Phase == BattlePhase.Training;
        public bool IsTraining => Phase == BattlePhase.Training;
        public bool IsEnded    => Phase == BattlePhase.Ended;

        public CharacterData Character1 { get; private set; }
        public CharacterData Character2 { get; private set; }

        public event System.Action<float>        OnTimerChanged;
        public event System.Action<float>        OnCountdownChanged;
        public event System.Action               OnBattleStart;
        public event System.Action               OnTrainingStart;
        public event System.Action<int>          OnBattleEnd;        // 0=1P wins, 1=2P wins, -1=draw
        public event System.Action               OnReturnedToSetup;  // リトライ時にSetupフェーズへ戻ったとき

        Coroutine _trainingResetRoutine;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            if (fighter1 != null) fighter1.OnDeath += () => HandleFighterDeath(1);
            if (fighter2 != null) fighter2.OnDeath += () => HandleFighterDeath(0);

            // 相手参照をセット（AutoFaceOpponent用）
            if (fighter1 != null && fighter2 != null)
            {
                fighter1.Opponent = fighter2;
                fighter2.Opponent = fighter1;
            }

            // ゲーム開始直後にデフォルト画像を適用（StartCountdown前でも表示）
            var defaultData = new CharacterData();
            ApplySprite(fighter1, defaultData);
            ApplySprite(fighter2, new CharacterData());

            // Setup中はファイターを非アクティブな位置でスポーン
            fighter1?.ResetForBattle(fighter1SpawnPos, faceRight: true);
            fighter2?.ResetForBattle(fighter2SpawnPos, faceRight: false);
        }

        void Update()
        {
            switch (Phase)
            {
                case BattlePhase.Countdown:
                    Countdown -= Time.deltaTime;
                    OnCountdownChanged?.Invoke(Countdown);
                    if (Countdown <= 0f) BeginFighting();
                    break;

                case BattlePhase.Fighting:
                    TimeRemaining = Mathf.Max(0f, TimeRemaining - Time.deltaTime);
                    OnTimerChanged?.Invoke(TimeRemaining);
                    if (TimeRemaining <= 0f) EndByTimeout();
                    break;
            }
        }

        // Setup完了→カウントダウン開始（PreBattlePanelから呼ぶ）
        public void StartCountdown(CharacterData data1, CharacterData data2)
        {
            if (Phase != BattlePhase.Setup) return;

            ApplyCharacters(data1, data2);

            Phase     = BattlePhase.Countdown;
            Countdown = countdownLength;
            OnCountdownChanged?.Invoke(Countdown);
        }

        public void StartTraining(CharacterData data1, CharacterData data2)
        {
            if (Phase != BattlePhase.Setup && Phase != BattlePhase.Training) return;

            ApplyCharacters(data1, data2);
            Phase = BattlePhase.Training;
            TimeRemaining = 0f;
            OnTimerChanged?.Invoke(TimeRemaining);
            OnTrainingStart?.Invoke();
            Debug.Log("[Battle] Training start");
        }

        public void ResetTrainingRound()
        {
            if (Phase != BattlePhase.Training) return;
            ResetFightersAndCooldowns();
        }

        void BeginFighting()
        {
            Phase         = BattlePhase.Fighting;
            TimeRemaining = battleDuration;
            OnBattleStart?.Invoke();
            Debug.Log("[Battle] FIGHT!");
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
            if (Phase == BattlePhase.Ended) return;
            if (Phase == BattlePhase.Training) return;
            Phase = BattlePhase.Ended;
            OnBattleEnd?.Invoke(winnerIndex);
            Debug.Log($"[Battle] {(winnerIndex < 0 ? "Draw" : $"{winnerIndex + 1}P Wins")}");
        }

        void HandleFighterDeath(int winnerIndex)
        {
            if (Phase == BattlePhase.Training)
            {
                if (_trainingResetRoutine == null)
                    _trainingResetRoutine = StartCoroutine(ResetTrainingAfterDelay());
                return;
            }

            EndBattle(winnerIndex);
        }

        IEnumerator ResetTrainingAfterDelay()
        {
            yield return new WaitForSeconds(trainingRespawnDelay);
            _trainingResetRoutine = null;
            if (Phase == BattlePhase.Training)
                ResetFightersAndCooldowns();
        }

        void ApplyCharacters(CharacterData data1, CharacterData data2)
        {
            Character1 = data1;
            Character2 = data2;

            fighter1?.GetComponent<SkillExecutor>()?.LoadCharacter(data1);
            fighter2?.GetComponent<SkillExecutor>()?.LoadCharacter(data2);

            ApplySprite(fighter1, data1);
            ApplySprite(fighter2, data2);

            ResetFightersAndCooldowns();
        }

        void ResetFightersAndCooldowns()
        {
            fighter1?.ResetForBattle(fighter1SpawnPos, faceRight: true);
            fighter2?.ResetForBattle(fighter2SpawnPos, faceRight: false);
            fighter1?.GetComponent<SkillExecutor>()?.ResetCooldowns();
            fighter2?.GetComponent<SkillExecutor>()?.ResetCooldowns();
        }

        static void ApplySprite(Fighter fighter, CharacterData data)
        {
            if (fighter == null || data == null) return;
            var sr = fighter.GetComponent<SpriteRenderer>();
            if (sr == null) return;

            // characterSpriteが既にセットされていればそちらを優先
            if (data.characterSprite != null)
            {
                sr.sprite = data.characterSprite;
                return;
            }

            if (string.IsNullOrEmpty(data.spritePath)) return;

            Sprite loaded = SpriteLoader.LoadWithWhiteBgRemoved(data.spritePath);
            if (loaded != null)
            {
                data.characterSprite = loaded;
                sr.sprite            = loaded;
            }
        }

        // リスタート（BattleResultUIから呼ぶ）
        public void ReturnToSetup()
        {
            if (_trainingResetRoutine != null)
            {
                StopCoroutine(_trainingResetRoutine);
                _trainingResetRoutine = null;
            }

            Phase = BattlePhase.Setup;
            fighter1?.ResetForBattle(fighter1SpawnPos, faceRight: true);
            fighter2?.ResetForBattle(fighter2SpawnPos, faceRight: false);
            OnReturnedToSetup?.Invoke();
        }
    }
}
