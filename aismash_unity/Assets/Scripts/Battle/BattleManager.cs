using UnityEngine;
using PromptFighters.Battle.Skills;
using PromptFighters.Utils;

namespace PromptFighters.Battle
{
    public enum BattlePhase
    {
        Setup,      // 名前入力・プリセット選択中
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

        [Header("References")]
        public Fighter fighter1;
        public Fighter fighter2;
        public Vector3 fighter1SpawnPos = new Vector3(-4f, 1f, 0f);
        public Vector3 fighter2SpawnPos = new Vector3( 4f, 1f, 0f);

        public float       TimeRemaining { get; private set; }
        public BattlePhase Phase         { get; private set; } = BattlePhase.Setup;
        public float       Countdown     { get; private set; }

        // 旧 BattleState 互換プロパティ
        public bool IsFighting => Phase == BattlePhase.Fighting;
        public bool IsEnded    => Phase == BattlePhase.Ended;

        public CharacterData Character1 { get; private set; }
        public CharacterData Character2 { get; private set; }

        public event System.Action<float>        OnTimerChanged;
        public event System.Action<float>        OnCountdownChanged;
        public event System.Action               OnBattleStart;
        public event System.Action<int>          OnBattleEnd;        // 0=1P wins, 1=2P wins, -1=draw
        public event System.Action               OnReturnedToSetup;  // リトライ時にSetupフェーズへ戻ったとき

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            if (fighter1 != null) fighter1.OnDeath += () => EndBattle(1);
            if (fighter2 != null) fighter2.OnDeath += () => EndBattle(0);

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

            Character1 = data1;
            Character2 = data2;

            // SkillExecutorにキャラデータを適用
            fighter1?.GetComponent<SkillExecutor>()?.LoadCharacter(data1);
            fighter2?.GetComponent<SkillExecutor>()?.LoadCharacter(data2);

            // スプライト読み込み・適用
            ApplySprite(fighter1, data1);
            ApplySprite(fighter2, data2);

            // 技情報を反映してからリセット
            fighter1?.ResetForBattle(fighter1SpawnPos, faceRight: true);
            fighter2?.ResetForBattle(fighter2SpawnPos, faceRight: false);

            Phase     = BattlePhase.Countdown;
            Countdown = countdownLength;
            OnCountdownChanged?.Invoke(Countdown);
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
            Phase = BattlePhase.Ended;
            OnBattleEnd?.Invoke(winnerIndex);
            Debug.Log($"[Battle] {(winnerIndex < 0 ? "Draw" : $"{winnerIndex + 1}P Wins")}");
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
            Phase = BattlePhase.Setup;
            fighter1?.ResetForBattle(fighter1SpawnPos, faceRight: true);
            fighter2?.ResetForBattle(fighter2SpawnPos, faceRight: false);
            OnReturnedToSetup?.Invoke();
        }
    }
}
