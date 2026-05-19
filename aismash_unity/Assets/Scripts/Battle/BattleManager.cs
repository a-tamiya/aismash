using UnityEngine;
using UnityEngine.InputSystem;
using PromptFighters.Battle.Skills;
using PromptFighters.Utils;
using PromptFighters.UI;
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
        public float battleDuration  = 180f;
        public float countdownLength = 3f;
        public float trainingRespawnDelay = 1f;

        [Header("References")]
        public Fighter fighter1;
        public Fighter fighter2;
        public Vector3 fighter1SpawnPos = new Vector3(-4f, -1.8f, 0f);
        public Vector3 fighter2SpawnPos = new Vector3( 4f, -1.8f, 0f);
        public Vector3 nameplateOffset = new Vector3(0f, 2.35f, 0f);
        public float stageHalfWidth = 6.5f;
        [Range(0.6f, 1.2f)] public float fighterScale = 1.12f;

        public float       TimeRemaining { get; private set; }
        public BattlePhase Phase         { get; private set; } = BattlePhase.Setup;
        public float       Countdown     { get; private set; }
        public float       StageMinX     => -stageHalfWidth;
        public float       StageMaxX     =>  stageHalfWidth;

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
            battleDuration = Mathf.Max(battleDuration, 180f);
            fighterScale = Mathf.Clamp(fighterScale, 1.08f, 1.12f);
            if (GetComponent<BattleLogger>() == null)
                gameObject.AddComponent<BattleLogger>();
            if (GetComponent<BattleDebugTuner>() == null)
                gameObject.AddComponent<BattleDebugTuner>();
            if (GetComponent<CommentaryController>() == null)
                gameObject.AddComponent<CommentaryController>();
            if (GetComponent<AngelController>() == null)
                gameObject.AddComponent<AngelController>();
        }

        void Start()
        {
            if (fighter1 != null) fighter1.OnDeath += () => HandleFighterDeath(1);
            if (fighter2 != null) fighter2.OnDeath += () => HandleFighterDeath(0);
            EnsureNameplate(fighter1, "P1", new Color(0.4f, 0.75f, 1f));
            EnsureNameplate(fighter2, "P2", new Color(1f, 0.55f, 0.35f));

            // 相手参照をセット（つかみ・状態異常・AIコメント用）
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
            ApplyFighterScale(fighter1);
            ApplyFighterScale(fighter2);
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
            ResetFightersAndSkillState();
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
                ResetFightersAndSkillState();
        }

        void ApplyCharacters(CharacterData data1, CharacterData data2)
        {
            Character1 = data1;
            Character2 = data2;

            fighter1?.GetComponent<SkillExecutor>()?.LoadCharacter(data1);
            fighter2?.GetComponent<SkillExecutor>()?.LoadCharacter(data2);
            fighter1?.ApplyCharacterStats(data1?.stats);
            fighter2?.ApplyCharacterStats(data2?.stats);
            fighter1?.SetGrabThrowParameters(data1?.grabParameters, data1?.throwParameters);
            fighter2?.SetGrabThrowParameters(data2?.grabParameters, data2?.throwParameters);

            ApplySprite(fighter1, data1);
            ApplySprite(fighter2, data2);

            ResetFightersAndSkillState();
        }

        void ResetFightersAndSkillState()
        {
            ApplyFighterScale(fighter1);
            ApplyFighterScale(fighter2);
            fighter1?.ResetForBattle(fighter1SpawnPos, faceRight: true);
            fighter2?.ResetForBattle(fighter2SpawnPos, faceRight: false);
            fighter1?.GetComponent<SkillExecutor>()?.ResetSkillState();
            fighter2?.GetComponent<SkillExecutor>()?.ResetSkillState();
        }

        void ApplyFighterScale(Fighter fighter)
        {
            if (fighter == null) return;
            float sign = fighter.transform.localScale.x < 0f ? -1f : 1f;
            fighter.transform.localScale = new Vector3(sign * fighterScale, fighterScale, fighter.transform.localScale.z);
        }

        static void ApplySprite(Fighter fighter, CharacterData data)
        {
            if (fighter == null || data == null) return;

            // characterSpriteが既にセットされていればそちらを優先
            if (data.characterSprite != null)
            {
                data.spriteSet.Set(CharacterSpriteId.Idle1, data.characterSprite);
                fighter.SetCharacterSprites(data.spriteSet);
                fighter.SetCharacterSprite(data.characterSprite);
                return;
            }

            if (string.IsNullOrEmpty(data.spritePath)) return;

            Sprite loaded = SpriteLoader.LoadWithWhiteBgRemoved(data.spritePath);
            if (loaded != null)
            {
                data.SetPrimarySprite(loaded);
                fighter.SetCharacterSprites(data.spriteSet);
            }
        }

        void EnsureNameplate(Fighter fighter, string label, Color color)
        {
            if (fighter == null) return;

            PlayerNameplate plate = null;
            var existing = GameObject.Find($"{label}_Nameplate");
            if (existing != null) plate = existing.GetComponent<PlayerNameplate>();
            if (plate == null)
            {
                var go = new GameObject($"{label}_Nameplate");
                plate = go.AddComponent<PlayerNameplate>();
            }

            plate.SetTarget(fighter.transform, fighter.VisualRenderer, label, color, nameplateOffset);
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

    public class BattleDebugTuner : MonoBehaviour
    {
        bool _visible;
        int _selectedPlayer;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f12Key.wasPressedThisFrame)
                _visible = !_visible;
        }

        void OnGUI()
        {
            if (!_visible) return;

            var bm = BattleManager.Instance;
            if (bm == null) return;

            GUI.depth = -100;
            GUILayout.BeginArea(new Rect(16f, 16f, 330f, 430f), "DEBUG TUNER (F12)", GUI.skin.window);
            _selectedPlayer = GUILayout.Toolbar(_selectedPlayer, new[] { "1P", "2P" });

            Fighter fighter = _selectedPlayer == 0 ? bm.fighter1 : bm.fighter2;
            if (fighter == null)
            {
                GUILayout.Label("Fighter not assigned.");
                GUILayout.EndArea();
                return;
            }

            float maxHp = Slider("Max HP", fighter.maxHP, 1f, 300f);
            float currentHp = Slider("Current HP", fighter.CurrentHP, 0f, maxHp);
            float ground = Slider("Ground Speed", fighter.moveSpeed, 0f, 14f);
            float air = Slider("Air Speed", fighter.airMoveSpeed, 0f, 14f);
            float jump = Slider("Jump Force", fighter.jumpForce, 0f, 24f);
            float airJump = Slider("Double Jump Height", fighter.airJumpHeightMultiplier, 0.3f, 0.6f);
            float walk = Slider("Walk Ratio", fighter.walkSpeedRatio, 0.2f, 0.5f);
            float guard = Slider("Guard", fighter.maxGuardDurability, 1f, 150f);
            float weight = Slider("Weight", fighter.weight, 0.2f, 2.5f);

            fighter.DebugSetBattleStats(maxHp, ground, air, jump, guard, weight, walk, airJump);
            fighter.DebugSetCurrentHP(currentHp);

            GUILayout.Space(8f);
            if (GUILayout.Button("Reset Round"))
                bm.ResetTrainingRound();
            if (GUILayout.Button("Fill HP / Guard"))
            {
                fighter.DebugSetCurrentHP(fighter.maxHP);
                fighter.DebugSetCurrentGuard(fighter.maxGuardDurability);
                fighter.DebugSetBattleStats(fighter.maxHP, fighter.moveSpeed, fighter.airMoveSpeed,
                    fighter.jumpForce, fighter.maxGuardDurability, fighter.weight);
            }
            GUILayout.Label("Training and battle values update immediately.");
            GUILayout.EndArea();
        }

        static float Slider(string label, float value, float min, float max)
        {
            GUILayout.Label($"{label}: {value:0.##}");
            return GUILayout.HorizontalSlider(value, min, max);
        }
    }
}
