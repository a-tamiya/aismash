using UnityEngine;
using UnityEngine.InputSystem;
using PromptFighters.Battle.Skills;
using PromptFighters.Utils;
using PromptFighters.UI;
using PromptFighters.GameFlow;
using System.Collections;
using System.Collections.Generic;

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

    // バトル種別。Versus=従来の1v1。CoopVsBoss=2人(+AI仲間)でボスに挑む協力モード。
    public enum BattleMode { Versus, CoopVsBoss }

    public class BattleManager : MonoBehaviour
    {
        public static BattleManager Instance { get; private set; }

        [Header("Settings")]
        public float battleDuration  = 180f;
        public float countdownLength = 3f;
        public float trainingRespawnDelay = 1f;

        [Header("Round Settings")]
        public bool bestOf3 = true;

        [Header("References")]
        public Fighter fighter1;
        public Fighter fighter2;
        public Fighter boss;   // 協力モードの3体目（バトルシーンに事前配置し割り当てる）。Versusでは非アクティブ化。
        public Vector3 fighter1SpawnPos = new Vector3(-4f, -1.8f, 0f);
        public Vector3 fighter2SpawnPos = new Vector3( 4f, -1.8f, 0f);
        public Vector3 bossSpawnPos     = new Vector3( 0f, -1.8f, 0f);

        [Header("Coop Boss Tuning")]
        public float bossHpMultiplier   = 2.5f;  // プレイヤー基準HP(300)に対する倍率
        public float bossSizeScale      = 1.6f;  // 見た目サイズ倍率
        public float bossDamageScale    = 1.4f;  // 与ダメージ倍率
        const float  BaseBossHp         = 300f;  // 倍率の基準HP（プレイヤー既定と同じ）

        [Header("Coop Revive")]
        public float reviveRange        = 1.2f;  // ダウン味方を復活させる接近距離
        public float reviveHoldTime     = 1.2f;  // 復活に必要な滞在時間(秒)

        public Vector3 nameplateOffset = new Vector3(0f, 2.35f, 0f);
        public float stageHalfWidth = 6.5f;
        [Range(0.6f, 1.2f)] public float fighterScale = 1.12f;

        // キャラ選択画面の「協力」トグルが起動前に設定する。シーンロードを跨いでもよいよう static。
        public static BattleMode RequestedMode = BattleMode.Versus;

        // 協力モードでボスに使うキャラデータ。PreBattlePanelで選択して設定する。未設定ならプリセット先頭。
        public static CharacterData RequestedBossCharacter;

        // 現在のバトル種別（既定は従来の1v1）。協力モードのみ追加処理を有効化する。
        public BattleMode  Mode          { get; set; } = BattleMode.Versus;
        // 登場している全ファイター（協力モードのターゲット解決に使う）。
        public readonly List<Fighter> Fighters = new List<Fighter>();

        // Versus時のbestOf3初期値を保持（協力モードで一時的にfalseへ上書きするため）。
        bool _versusBestOf3 = true;
        // 一度きりのイベント購読が済んだか（モード切替で再購読しないため）。
        bool _eventsWired;

        // 毎フレームのGetComponentを避けるためAwakeでキャッシュ（Fighterの接地判定などが参照）。
        public StagePlatformSpawner PlatformSpawner { get; private set; }

        public float       TimeRemaining { get; private set; }
        public BattlePhase Phase         { get; private set; } = BattlePhase.Setup;
        public float       Countdown     { get; private set; }
        public float       StageMinX     => -stageHalfWidth;
        public float       StageMaxX     =>  stageHalfWidth;
        public float       StageGroundY  =>  fighter1SpawnPos.y; // 影の落下先となる地面の基準Y

        public int P1RoundWins  { get; private set; }
        public int P2RoundWins  { get; private set; }
        public int CurrentRound { get; private set; } = 1;

        // 旧 BattleState 互換プロパティ
        public bool IsFighting => Phase == BattlePhase.Fighting || Phase == BattlePhase.Training;
        public bool IsTraining => Phase == BattlePhase.Training;
        public bool IsEnded    => Phase == BattlePhase.Ended;

        public CharacterData Character1 { get; private set; }
        public CharacterData Character2 { get; private set; }
        public CharacterData BossCharacter { get; private set; }

        public event System.Action<float>        OnTimerChanged;
        public event System.Action<float>        OnCountdownChanged;
        public event System.Action               OnBattleStart;
        public event System.Action               OnTrainingStart;
        public event System.Action<int>          OnBattleEnd;        // 0=1P wins, 1=2P wins, -1=draw (マッチ終了)
        public event System.Action<int,int,int>  OnRoundEnd;         // winnerIdx, p1wins, p2wins
        public event System.Action<int>          OnRoundStart;       // round number
        public event System.Action               OnReturnedToSetup;  // リトライ時にSetupフェーズへ戻ったとき
        public event System.Action               OnKnockout;         // KO演出開始（全画面KO表示用）

        Coroutine _trainingResetRoutine;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            battleDuration = Mathf.Max(battleDuration, 180f);
            fighterScale = Mathf.Clamp(fighterScale, 1.08f, 1.12f);
            if (GetComponent<BattleLogger>() == null)
                gameObject.AddComponent<BattleLogger>();
            if (PromptFighters.Audio.GameAudioManager.Instance == null &&
                GetComponent<PromptFighters.Audio.GameAudioManager>() == null)
                gameObject.AddComponent<PromptFighters.Audio.GameAudioManager>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (GetComponent<BattleDebugTuner>() == null)
                gameObject.AddComponent<BattleDebugTuner>();
#endif
            if (GetComponent<CommentaryController>() == null)
                gameObject.AddComponent<CommentaryController>();
            if (GetComponent<AngelController>() == null)
                gameObject.AddComponent<AngelController>();
            if (GetComponent<ComboCounter>() == null)
                gameObject.AddComponent<ComboCounter>();
            if (GetComponent<StagePlatformSpawner>() == null)
                gameObject.AddComponent<StagePlatformSpawner>();
            PlatformSpawner = GetComponent<StagePlatformSpawner>();

            _mainCam = Camera.main;
            if (_mainCam != null)
            {
                _defaultCamOrthoSize = _mainCam.orthographicSize;
                _defaultCamPos       = _mainCam.transform.position;
                // 距離に応じた自動ズーム＆シェイク適用を担うバトルカメラを付与
                if (_mainCam.GetComponent<BattleCamera>() == null)
                    _mainCam.gameObject.AddComponent<BattleCamera>();
            }
        }

        void Start()
        {
            _versusBestOf3 = bestOf3;

            EnsureNameplate(fighter1, "P1", new Color(0.4f, 0.75f, 1f));
            EnsureNameplate(fighter2, "P2", new Color(1f, 0.55f, 0.35f));
            if (boss != null) EnsureNameplate(boss, "BOSS", new Color(1f, 0.3f, 0.3f));

            WireFighterEvents();
            ApplyMode();
            ApplyCpuControl();

            // ゲーム開始直後にデフォルト画像を適用（StartCountdown前でも表示）
            ApplySprite(fighter1, new CharacterData());
            ApplySprite(fighter2, new CharacterData());
            if (boss != null && Mode == BattleMode.CoopVsBoss) ApplySprite(boss, new CharacterData());

            // Setup中はファイターを非アクティブな位置でスポーン
            ApplyFighterScale(fighter1);
            ApplyFighterScale(fighter2);
            fighter1?.ResetForBattle(fighter1SpawnPos, faceRight: true);
            fighter2?.ResetForBattle(fighter2SpawnPos, faceRight: false);
            if (Mode == BattleMode.CoopVsBoss && boss != null)
            {
                ApplyFighterScale(boss);
                boss.ResetForBattle(bossSpawnPos, faceRight: false);
            }
        }

        // 一度きりのイベント購読。モード切替で再購読しないよう _eventsWired でガード。
        void WireFighterEvents()
        {
            if (_eventsWired) return;
            _eventsWired = true;

            if (fighter1 != null) { fighter1.OnDeath += () => HandleFighterDeath(1); fighter1.OnDowned += HandlePlayerDowned; }
            if (fighter2 != null) { fighter2.OnDeath += () => HandleFighterDeath(0); fighter2.OnDowned += HandlePlayerDowned; }
            if (boss != null)       boss.OnDeath     += HandleBossDeath;
        }

        // モード確定（バトル開始時に再実行可能）。陣営・ボス表示・bestOf3・ターゲット参照を切り替える。
        public void ApplyMode()
        {
            Mode = RequestedMode;
            bool coop = Mode == BattleMode.CoopVsBoss;

            if (fighter1 != null) fighter1.Team = FighterTeam.Players;
            if (fighter2 != null) fighter2.Team = coop ? FighterTeam.Players : FighterTeam.Enemies;
            if (boss != null)
            {
                boss.Team = FighterTeam.Enemies;
                boss.gameObject.SetActive(coop);
            }

            bestOf3 = coop ? false : _versusBestOf3;

            Fighters.Clear();
            if (fighter1 != null) Fighters.Add(fighter1);
            if (fighter2 != null) Fighters.Add(fighter2);
            if (coop && boss != null) Fighters.Add(boss);

            if (coop)
            {
                IgnoreAllFighterBodyCollisions();
            }
            else if (fighter1 != null && fighter2 != null)
            {
                fighter1.Opponent = fighter2;
                fighter2.Opponent = fighter1;
            }
        }

        // 登録された全ファイターのペアで体同士の当たりを無視する（押し合い防止）。
        void IgnoreAllFighterBodyCollisions()
        {
            for (int i = 0; i < Fighters.Count; i++)
                for (int j = i + 1; j < Fighters.Count; j++)
                    Fighters[i]?.IgnoreBodyCollisionWith(Fighters[j]);
        }

        // 操作主体を割り当てる。Versusは2P側のCPUトグルに従う。Coopは敵ボスと（1人時の）AI仲間をAIにする。
        void ApplyCpuControl()
        {
            // CPUが操作する側はロビーで選択（CpuSide: 1=1P, 2=2P）。もう一方は常に人間。
            Fighter cpuFighter   = FighterAI.CpuSide == 1 ? fighter1 : fighter2;
            Fighter humanFighter = FighterAI.CpuSide == 1 ? fighter2 : fighter1;

            if (Mode == BattleMode.CoopVsBoss)
            {
                // ボスは常にAI（敵）。難易度は味方トグルと独立に常に最高（Hard）固定。
                SetFighterAi(boss, enable: true, levelOverride: FighterAI.CpuLevel.Hard);
            }

            // 選択した側をCPU（ON時）に、もう一方は人間に。強さはトグル準拠。
            SetFighterAi(cpuFighter, enable: FighterAI.Enabled);
            SetFighterAi(humanFighter, enable: false);
        }

        // 指定ファイターをAI操作/人間操作に切り替える。
        // levelOverride 指定時はグローバルの難易度トグルと独立に強さを固定する。
        static void SetFighterAi(Fighter fighter, bool enable, FighterAI.CpuLevel? levelOverride = null)
        {
            if (fighter == null) return;
            var input = fighter.GetComponent<FighterInput>();
            var ai    = fighter.GetComponent<FighterAI>();
            if (enable)
            {
                if (ai == null) ai = fighter.gameObject.AddComponent<FighterAI>();
                ai.enabled = true;
                if (levelOverride.HasValue) ai.ApplyLevel(levelOverride.Value);
                else                        ai.ApplyLevel();
                if (input != null) input.enabled = false;
            }
            else
            {
                if (ai != null) ai.enabled = false;
                if (input != null) input.enabled = true;
            }
        }

        // 協力モード用：各ファイターの狙う相手を「最も近い生存中の敵陣営」に毎フレーム更新する。
        void RefreshOpponents()
        {
            for (int i = 0; i < Fighters.Count; i++)
            {
                var f = Fighters[i];
                if (f == null) continue;
                f.Opponent = NearestEnemy(f);
            }
        }

        // 協力モード用：ダウンした味方のそばに生存中の味方が一定時間いれば復活させる。
        readonly Dictionary<Fighter, float>       _reviveProgress = new Dictionary<Fighter, float>();
        readonly Dictionary<Fighter, ReviveGauge> _reviveGauges   = new Dictionary<Fighter, ReviveGauge>();
        void ReviveCheck()
        {
            for (int i = 0; i < Fighters.Count; i++)
            {
                var downed = Fighters[i];
                if (downed == null || !downed.IsDowned || downed.Team != FighterTeam.Players)
                {
                    if (downed != null) ClearReviveGauge(downed);
                    continue;
                }

                bool helperNear = false;
                for (int j = 0; j < Fighters.Count; j++)
                {
                    var helper = Fighters[j];
                    if (helper == null || helper == downed) continue;
                    if (helper.Team != FighterTeam.Players) continue;
                    if (helper.IsDowned || helper.State == FighterState.Dead) continue;
                    Vector3 d = helper.transform.position - downed.ReviveAnchorPosition;
                    if (Mathf.Abs(d.x) <= reviveRange && Mathf.Abs(d.y) <= reviveRange * 1.5f)
                    {
                        helperNear = true;
                        break;
                    }
                }

                float prog = _reviveProgress.TryGetValue(downed, out var p) ? p : 0f;
                if (helperNear)
                {
                    prog += Time.deltaTime;
                    if (prog >= reviveHoldTime)
                    {
                        downed.Revive(0.5f);
                        DamagePopup.SpawnText(downed.transform.position + Vector3.up * 1.0f,
                            "復活!", new Color(0.4f, 1f, 0.6f), 1.6f);
                        prog = 0f;
                        ClearReviveGauge(downed);
                    }
                    else
                    {
                        UpdateReviveGauge(downed, prog / reviveHoldTime);
                    }
                }
                else
                {
                    prog = 0f;
                    ClearReviveGauge(downed);
                }
                _reviveProgress[downed] = prog;
            }
        }

        void UpdateReviveGauge(Fighter downed, float t01)
        {
            if (!_reviveGauges.TryGetValue(downed, out var gauge) || gauge == null)
            {
                gauge = ReviveGauge.Create(downed);
                _reviveGauges[downed] = gauge;
            }
            gauge.SetProgress(t01);
        }

        void ClearReviveGauge(Fighter downed)
        {
            if (_reviveGauges.TryGetValue(downed, out var gauge))
            {
                if (gauge != null) Destroy(gauge.gameObject);
                _reviveGauges.Remove(downed);
            }
        }

        // self から見て、反対陣営で生存中（Dead/Downedでない）の最も近いファイターを返す。いなければnull。
        Fighter NearestEnemy(Fighter self)
        {
            Fighter best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < Fighters.Count; i++)
            {
                var f = Fighters[i];
                if (f == null || f == self) continue;
                if (f.Team == self.Team) continue;
                if (f.State == FighterState.Dead || f.IsDowned) continue;
                float d = Mathf.Abs(f.transform.position.x - self.transform.position.x);
                if (d < bestDist) { bestDist = d; best = f; }
            }
            return best;
        }

        void Update()
        {
            if (Keyboard.current?.f3Key.wasPressedThisFrame == true)
                DebugSettings.ShowHitboxes = !DebugSettings.ShowHitboxes;

            switch (Phase)
            {
                case BattlePhase.Countdown:
                    Countdown -= Time.deltaTime;
                    OnCountdownChanged?.Invoke(Countdown);
                    if (Countdown <= 0f) BeginFighting();
                    break;

                case BattlePhase.Fighting:
                    if (Mode == BattleMode.CoopVsBoss) { RefreshOpponents(); ReviveCheck(); }
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

            ApplyMode();
            GetComponent<StagePlatformSpawner>()?.SpawnPlatforms();
            ApplyCharacters(data1, data2);
            if (Mode == BattleMode.CoopVsBoss) ApplyBoss();
            ApplyCpuControl();

            Phase     = BattlePhase.Countdown;
            Countdown = countdownLength;
            OnCountdownChanged?.Invoke(Countdown);
        }

        public void StartTraining(CharacterData data1, CharacterData data2)
        {
            if (Phase != BattlePhase.Setup && Phase != BattlePhase.Training) return;

            ApplyMode();
            ApplyCharacters(data1, data2);
            ApplyCpuControl();
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
            if (Mode == BattleMode.CoopVsBoss && boss != null) StartCoroutine(BossEntrancePulse());
            Debug.Log("[Battle] FIGHT!");
        }

        // ボス登場演出：一瞬大きくして着地するようにスケールを戻す。
        IEnumerator BossEntrancePulse()
        {
            if (boss == null) yield break;
            Vector3 baseScale = boss.transform.localScale;
            float magX = Mathf.Abs(baseScale.x);
            float dur = 0.45f;
            float t = 0f;
            DamagePopup.SpawnText(boss.transform.position + Vector3.up * 1.6f,
                "BOSS", new Color(1f, 0.3f, 0.3f), 2.0f);
            while (t < dur && boss != null)
            {
                t += Time.deltaTime;
                float k = 1f + 0.35f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur));
                float sign = boss.FacingRight ? 1f : -1f;
                boss.transform.localScale = new Vector3(sign * magX * k, baseScale.y * k, baseScale.z);
                yield return null;
            }
            if (boss != null)
            {
                float sign = boss.FacingRight ? 1f : -1f;
                boss.transform.localScale = new Vector3(sign * magX, baseScale.y, baseScale.z);
            }
        }

        void EndByTimeout()
        {
            // 協力モードは時間切れ＝ボスの勝利（プレイヤー敗北）。
            if (Mode == BattleMode.CoopVsBoss) { CoopEnd(playersWin: false); return; }
            if (fighter1 == null || fighter2 == null) { FinishRoundOrMatch(-1); return; }
            // maxHPはキャラ毎に異なる（250〜350）ため、絶対値ではなく残りHP割合で比較する
            float r1 = fighter1.CurrentHP / Mathf.Max(1f, fighter1.MaxHP);
            float r2 = fighter2.CurrentHP / Mathf.Max(1f, fighter2.MaxHP);
            if      (r1 > r2) FinishRoundOrMatch(0);
            else if (r2 > r1) FinishRoundOrMatch(1);
            else              FinishRoundOrMatch(-1);
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
            if (Mode == BattleMode.CoopVsBoss)
            {
                // 協力モードは原則ダウンで判定するが、万一プレイヤーが死亡経路に入った場合も全滅判定を通す。
                HandlePlayerDowned();
                return;
            }

            if (Phase == BattlePhase.Training)
            {
                if (_trainingResetRoutine == null)
                    _trainingResetRoutine = StartCoroutine(ResetTrainingAfterDelay());
                return;
            }

            FinishRoundOrMatch(winnerIndex);
        }

        void FinishRoundOrMatch(int winnerIndex)
        {
            if (Phase == BattlePhase.Ended) return;
            if (Phase == BattlePhase.Training) return;
            Phase = BattlePhase.Ended;

            if (!bestOf3)
            {
                EndBattle(winnerIndex);
                return;
            }

            if (winnerIndex == 0)      P1RoundWins++;
            else if (winnerIndex == 1) P2RoundWins++;

            OnRoundEnd?.Invoke(winnerIndex, P1RoundWins, P2RoundWins);
            Debug.Log($"[Battle] Round {CurrentRound} end. P1:{P1RoundWins} P2:{P2RoundWins}");

            if (P1RoundWins >= 2 || P2RoundWins >= 2)
            {
                int matchWinner = P1RoundWins >= 2 ? 0 : 1;
                Debug.Log($"[Battle] Match over. {matchWinner + 1}P Wins");
                OnBattleEnd?.Invoke(matchWinner); // Phase は既に Ended なので EndBattle を使わず直接発火
            }
            else
            {
                StartCoroutine(NextRoundRoutine());
            }
        }

        // ===== 協力モードの勝敗（CoopVsBoss） =====
        // winnerIndex は 0=プレイヤー陣営の勝ち / 1=ボスの勝ち として OnBattleEnd を発火する。

        // ボス（Enemies）撃破でプレイヤー勝利。OnDeathから呼ぶ（subtask4で配線）。
        public void HandleBossDeath()
        {
            if (Mode != BattleMode.CoopVsBoss) return;
            CoopEnd(playersWin: true);
        }

        // Players陣営のいずれかがダウンした際に呼ぶ。全員ダウンなら敗北。OnDownedから配線（subtask4）。
        public void HandlePlayerDowned()
        {
            if (Mode != BattleMode.CoopVsBoss) return;
            bool anyAlive = false;
            for (int i = 0; i < Fighters.Count; i++)
            {
                var f = Fighters[i];
                if (f == null || f.Team != FighterTeam.Players) continue;
                if (!f.IsDowned && f.State != FighterState.Dead) { anyAlive = true; break; }
            }
            if (!anyAlive) CoopEnd(playersWin: false);
        }

        void CoopEnd(bool playersWin)
        {
            if (Phase == BattlePhase.Ended || Phase == BattlePhase.Training) return;
            Phase = BattlePhase.Ended;
            OnBattleEnd?.Invoke(playersWin ? 0 : 1);
            Debug.Log($"[Battle] Coop {(playersWin ? "Players Win" : "Boss Wins")}");
        }

        IEnumerator NextRoundRoutine()
        {
            // KOスローが終わるまで待つ
            yield return new WaitForSecondsRealtime(3.8f);

            Time.timeScale = 1f;
            _hitStopActive = false;
            _koSlowActive  = false;
            ResetCameraZoom();
            CurrentRound++;

            ResetFightersAndSkillState();
            yield return null; // 1フレーム待ってRigidbody2Dの位置を確定させる

            Phase     = BattlePhase.Countdown;
            Countdown = countdownLength;
            OnRoundStart?.Invoke(CurrentRound);
            OnCountdownChanged?.Invoke(Countdown);
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

            // 新しいマッチではギミックの永続倍率（巨大化・速度等）を初期化してから基準値を適用する。
            fighter1?.ResetGimmickStats();
            fighter2?.ResetGimmickStats();

            fighter1?.GetComponent<SkillExecutor>()?.LoadCharacter(data1);
            fighter2?.GetComponent<SkillExecutor>()?.LoadCharacter(data2);
            fighter1?.ApplyCharacterStats(data1?.stats);
            fighter2?.ApplyCharacterStats(data2?.stats);
            fighter1?.SetGrabThrowParameters(data1?.grabParameters, data1?.throwParameters);
            fighter2?.SetGrabThrowParameters(data2?.grabParameters, data2?.throwParameters);
            fighter1?.SetSizeScale(data1?.sizeScale ?? 1f);
            fighter2?.SetSizeScale(data2?.sizeScale ?? 1f);

            ApplySprite(fighter1, data1);
            ApplySprite(fighter2, data2);

            ResetFightersAndSkillState();
        }

        // 協力モードのボスにプリセットの技・ステータス・見た目を適用し、HP/サイズ/与ダメージを強化する。
        void ApplyBoss()
        {
            if (boss == null) return;

            var data = BuildBossCharacter();
            EnsureBossSpriteSet(data);
            BossCharacter = data;

            boss.ResetGimmickStats();
            boss.GetComponent<SkillExecutor>()?.LoadCharacter(data);
            boss.ApplyCharacterStats(data.stats);
            boss.SetGrabThrowParameters(data.grabParameters, data.throwParameters);
            boss.SetSizeScale((data.sizeScale > 0f ? data.sizeScale : 1f) * bossSizeScale);
            ApplySprite(boss, data);

            boss.maxHP          = Mathf.Clamp(BaseBossHp * bossHpMultiplier, 1f, 4000f);
            boss.BossDamageScale = Mathf.Max(0.1f, bossDamageScale);

            ApplyFighterScale(boss);
            boss.ResetForBattle(bossSpawnPos, faceRight: false);
            boss.GetComponent<SkillExecutor>()?.ResetSkillState();
        }

        // ボスのプリセットはエフェクト/ポーズ用スプライトが未ロードのことがあり、技が四角で表示される。
        // 保存先からフルロードして技エフェクトのスプライトを揃える（プレイヤーの EnsureSpriteSet 相当）。
        static void EnsureBossSpriteSet(CharacterData data)
        {
            if (data == null) return;
            if (HasPoseAndEffectSprites(data.spriteSet)) return;
            if (string.IsNullOrEmpty(data.spriteDir)) return;

            var loaded = CharacterSaveManager.LoadSpriteSet(data.spriteDir);
            if (loaded == null) return;

            data.spriteSet = loaded;
            if (data.characterSprite == null)
                data.characterSprite = loaded.Get(CharacterSpriteId.Idle1);
        }

        // pose/effect スプライト（Jump 以降 = index 3..）が1枚でもあるか。idle1/2/3 は対象外。
        static bool HasPoseAndEffectSprites(CharacterSpriteSet spriteSet)
        {
            if (spriteSet?.sprites == null) return false;
            for (int i = (int)CharacterSpriteId.Jump; i < spriteSet.sprites.Length; i++)
                if (spriteSet.sprites[i] != null) return true;
            return false;
        }

        // ボス用のキャラデータを用意する。選択済みがあればそれを、無ければプリセット先頭、最後に素のデータを返す。
        CharacterData BuildBossCharacter()
        {
            if (RequestedBossCharacter != null)
                return RequestedBossCharacter;
            var presets = PresetCharacterLoader.LoadAll();
            if (presets != null && presets.Count > 0 && presets[0] != null)
                return presets[0];
            return new CharacterData { characterName = "BOSS" };
        }

        void ResetFightersAndSkillState()
        {
            ApplyFighterScale(fighter1);
            ApplyFighterScale(fighter2);
            fighter1?.ResetForBattle(fighter1SpawnPos, faceRight: true);
            fighter2?.ResetForBattle(fighter2SpawnPos, faceRight: false);
            fighter1?.GetComponent<SkillExecutor>()?.ResetSkillState();
            fighter2?.GetComponent<SkillExecutor>()?.ResetSkillState();

            if (Mode == BattleMode.CoopVsBoss && boss != null)
            {
                ApplyFighterScale(boss);
                boss.ResetForBattle(bossSpawnPos, faceRight: false);
                boss.GetComponent<SkillExecutor>()?.ResetSkillState();
            }
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

            StopAllCoroutines();
            _hitStopActive = false;
            _koSlowActive  = false;
            Time.timeScale = 1f;
            Phase = BattlePhase.Setup;
            P1RoundWins  = 0;
            P2RoundWins  = 0;
            CurrentRound = 1;
            // 前マッチのギミック倍率（巨大化・速度等）を持ち越さない。
            fighter1?.ResetGimmickStats();
            fighter2?.ResetGimmickStats();
            boss?.ResetGimmickStats();
            fighter1?.ResetForBattle(fighter1SpawnPos, faceRight: true);
            fighter2?.ResetForBattle(fighter2SpawnPos, faceRight: false);
            ResetCameraZoom();
            OnReturnedToSetup?.Invoke();
        }

        // ── ヒットストップ（Feature A）────────────────────────────────
        bool _hitStopActive;
        float _hitStopUntilRealtime;

        public void TriggerHitStop(float duration, float timeScale = 0.05f)
        {
            if (_koSlowActive) return; // KOスロー優先
            float until = Time.realtimeSinceStartup + duration;
            if (_hitStopActive)
            {
                // 実行中はより長い要求なら延長する（スマッシュの長停止が通常ヒットに潰されないように）
                if (until > _hitStopUntilRealtime) _hitStopUntilRealtime = until;
                return;
            }
            _hitStopUntilRealtime = until;
            StartCoroutine(HitStopCoroutine(timeScale));
        }

        IEnumerator HitStopCoroutine(float timeScale)
        {
            _hitStopActive = true;
            Time.timeScale = timeScale;
            while (Time.realtimeSinceStartup < _hitStopUntilRealtime && !_koSlowActive)
                yield return null;
            if (!_koSlowActive) // KOスロー中は timeScale を上書きしない
            {
                Time.timeScale = 1f;
                _hitStopActive = false;
            }
        }

        // ── KO時スロー＆カメラズーム（Feature H）──────────────────────
        Camera  _mainCam;
        float   _defaultCamOrthoSize;
        Vector3 _defaultCamPos;
        bool    _koSlowActive;

        // KOスロー演出中か（BattleCameraが追従を一時停止する判定に使う）
        public bool IsKoSlowActive => _koSlowActive;

        public void TriggerKOSlow(Vector3 koPosition)
        {
            if (_koSlowActive) return;
            StartCoroutine(KOSlowCoroutine(koPosition));
        }

        IEnumerator KOSlowCoroutine(Vector3 koPosition)
        {
            _koSlowActive  = true;
            _hitStopActive = true; // HitStopが上書きしないようにロック

            // KO演出: 大きな揺れ＋全画面KO表示（ko.png）
            CameraShake.Shake(0.5f, 0.5f);
            OnKnockout?.Invoke();

            float slowDuration = 2.5f;
            float zoomDuration = 0.25f;
            // 動的ズームで既に寄っている場合はそれより引かない（KO演出が「ズームアウト」に見えないように）
            float zoomInSize   = Mathf.Min(
                _mainCam != null ? _mainCam.orthographicSize : _defaultCamOrthoSize,
                _defaultCamOrthoSize * 0.70f);

            Time.timeScale = 0.15f;

            // カメラをKO位置へズームイン
            if (_mainCam != null)
            {
                Vector3 targetPos = new Vector3(
                    Mathf.Clamp(koPosition.x, -stageHalfWidth * 0.6f, stageHalfWidth * 0.6f),
                    _mainCam.transform.position.y,
                    _mainCam.transform.position.z);
                float elapsed = 0f;
                Vector3 startPos = _mainCam.transform.position;
                float startSize  = _mainCam.orthographicSize;
                while (elapsed < zoomDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t  = Mathf.SmoothStep(0f, 1f, elapsed / zoomDuration);
                    _mainCam.orthographicSize = Mathf.Lerp(startSize, zoomInSize, t);
                    _mainCam.transform.position = Vector3.Lerp(startPos, targetPos, t);
                    yield return null;
                }
            }

            yield return new WaitForSecondsRealtime(slowDuration);

            Time.timeScale = 1f;
            _hitStopActive = false;
            _koSlowActive  = false;
            // カメラはロビーに戻るまでズームを維持する（ResetCameraZoomは呼ばない）
        }

        void ResetCameraZoom()
        {
            if (_mainCam == null) return;
            _mainCam.orthographicSize = _defaultCamOrthoSize;
            _mainCam.transform.position = _defaultCamPos;
        }
    }

    public class BattleDebugTuner : MonoBehaviour
    {
        bool _visible;
        int _selectedPlayer;
        Vector2 _scroll;

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
            GUILayout.BeginArea(new Rect(16f, 16f, 360f, 620f), "DEBUG TUNER (F12)", GUI.skin.window);
            _scroll = GUILayout.BeginScrollView(_scroll);
            _selectedPlayer = GUILayout.Toolbar(_selectedPlayer, new[] { "1P", "2P" });

            Fighter fighter = _selectedPlayer == 0 ? bm.fighter1 : bm.fighter2;
            Fighter opponent = _selectedPlayer == 0 ? bm.fighter2 : bm.fighter1;
            if (fighter == null)
            {
                GUILayout.Label("Fighter not assigned.");
                GUILayout.EndScrollView();
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
            float guard = Slider("Guard", fighter.maxGuardDurability, 1f, 300f);
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

            GUILayout.Space(10f);
            GUILayout.Label("Debug Skills");
            var executor = fighter.GetComponent<SkillExecutor>();
            if (executor == null)
            {
                GUILayout.Label("SkillExecutor not found.");
            }
            else
            {
                ButtonSkill(executor, "Body Melee", DebugBodyMelee());
                ButtonSkill(executor, "Ranged Projectile", DebugProjectile());
                ButtonSkill(executor, "Trap / Slow Field", DebugTrap());
                ButtonSkill(executor, "Dash Attack", DebugDashAttack());
                ButtonSkill(executor, "Teleport Forward", DebugTeleport("forward"));
                ButtonSkill(executor, "Teleport Back", DebugTeleport("backward"));
                ButtonSkill(executor, "Push Enemy", DebugPush());
                ButtonSkill(executor, "Pull Enemy", DebugPull());
                ButtonSkill(executor, "Buff Speed", DebugBuff("speed"));
                ButtonSkill(executor, "Buff Jump", DebugBuff("jump"));
                ButtonSkill(executor, "Buff Transparent", DebugBuff("transparent"));
                ButtonSkill(executor, "Buff Damage", DebugBuff("damage"));
                ButtonSkill(executor, "Command Throw", DebugCommandThrow());
                ButtonSkill(executor, "Barrier", DebugBarrier());
                ButtonSkill(executor, "Shockwave", DebugShockwave());
                ButtonSkill(executor, "Gravity Well", DebugGravityWell());
                ButtonSkill(executor, "Lifesteal", DebugLifesteal());
                ButtonSkill(executor, "Heal Self", DebugHealSelf());
            }

            GUILayout.Space(8f);
            GUILayout.Label("Apply Status To Opponent");
            if (opponent == null)
            {
                GUILayout.Label("Opponent not assigned.");
            }
            else
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("STUN")) opponent.ApplyStatus(StatusType.Stun, 0.8f);
                if (GUILayout.Button("BURN")) opponent.ApplyStatus(StatusType.Burn, 4f);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("SLOW")) opponent.ApplyStatus(StatusType.Slow, 4f);
                if (GUILayout.Button("GUARD BREAK")) opponent.ApplyStatus(StatusType.GuardBreak, 1.8f);
                GUILayout.EndHorizontal();
            }

            GUILayout.Label("Training and battle values update immediately.");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        static float Slider(string label, float value, float min, float max)
        {
            GUILayout.Label($"{label}: {value:0.##}");
            return GUILayout.HorizontalSlider(value, min, max);
        }

        static void ButtonSkill(SkillExecutor executor, string label, SkillData skill)
        {
            if (GUILayout.Button(label))
                executor.TryUseDebugSkill(skill);
        }

        static SkillParameters Params(float damage, float range, float startup, float active, float recovery,
                                      int hits, float knockback, float stun = 0.08f, float guard = 0.5f) =>
            new SkillParameters
            {
                damage = damage,
                range = range,
                startup = startup,
                active_time = active,
                recovery = recovery,
                hit_count = hits,
                knockback = knockback,
                stun_time = stun,
                guard_damage = guard,
            };

        static SkillData DebugBodyMelee() => new SkillData
        {
            slot = SkillSlot.AttackA,
            skill_name = "DBG 体術近接",
            description = "エフェクトなし本体判定",
            element = Element.Physical,
            risk_level = RiskLevel.Low,
            parameters = Params(2f, 0.9f, 0.02f, 0.16f, 0.18f, 3, 2.5f),
            actions = new List<SkillAction>
            {
                new SkillAction { type = "body_hitbox", time = 0.02f, range = 0.9f, size_y = 1.25f, spawn_x = 0.55f, spawn_y = 0.65f, hit_count = 3, follow_owner = true, hide_effect = true },
            },
        };

        static SkillData DebugProjectile() => new SkillData
        {
            slot = SkillSlot.AttackB,
            skill_name = "DBG 遠距離弾",
            description = "低火力の火球",
            element = Element.Fire,
            risk_level = RiskLevel.Medium,
            parameters = Params(5f, 12f, 0.04f, 0.08f, 0.28f, 1, 3.5f, 0.05f, 0.6f),
            actions = new List<SkillAction>
            {
                new SkillAction { type = "projectile", time = 0.04f, spawn_x = 0.9f, spawn_y = 0.9f, size_x = 1.2f, size_y = 0.7f, projectile_speed = 10f, projectile_lifetime = 1.5f, status = "burn", duration = 2f, chance = 1f },
            },
        };

        static SkillData DebugTrap() => new SkillData
        {
            slot = SkillSlot.AttackC,
            skill_name = "DBG 設置スロー",
            description = "5秒残る低火力罠",
            element = Element.Ice,
            risk_level = RiskLevel.Medium,
            parameters = Params(2f, 2.4f, 0.05f, 0.1f, 0.24f, 1, 2f, 0.05f, 0.5f),
            actions = new List<SkillAction>
            {
                new SkillAction { type = "trap_hitbox", time = 0.05f, duration = 5f, spawn_x = 1.6f, spawn_y = 0.35f, size_x = 2.4f, size_y = 0.9f, status = "slow", chance = 1f },
            },
        };

        static SkillData DebugDashAttack() => new SkillData
        {
            slot = SkillSlot.AttackC,
            skill_name = "DBG 突進攻撃",
            description = "移動しながら近接",
            element = Element.Lightning,
            risk_level = RiskLevel.Medium,
            parameters = Params(4f, 1.25f, 0.03f, 0.12f, 0.32f, 1, 4f, 0.18f, 0.7f),
            actions = new List<SkillAction>
            {
                new SkillAction { type = "dash", time = 0f, power = 7f, direction = "forward" },
                new SkillAction { type = "melee_hitbox", time = 0.04f, range = 1.25f, size_y = 1.1f, spawn_x = 1.0f, spawn_y = 0.55f, follow_owner = true, status = "stun", duration = 0.35f, chance = 1f },
            },
        };

        static SkillData DebugTeleport(string direction) => new SkillData
        {
            slot = SkillSlot.AttackC,
            skill_name = direction == "backward" ? "DBG 後方テレポート" : "DBG 前方テレポート",
            description = "短距離ワープ",
            element = Element.Dark,
            risk_level = RiskLevel.Low,
            parameters = Params(0f, 0.5f, 0f, 0.02f, 0.12f, 1, 0f, 0f, 0f),
            actions = new List<SkillAction>
            {
                new SkillAction { type = "teleport", time = 0f, power = 2.8f, direction = direction },
            },
        };

        static SkillData DebugPush() => new SkillData
        {
            slot = SkillSlot.AttackC,
            skill_name = "DBG 押し出し",
            description = "風で相手を押す",
            element = Element.Wind,
            risk_level = RiskLevel.Low,
            parameters = Params(0f, 2.3f, 0.02f, 0.08f, 0.22f, 1, 0f, 0f, 0f),
            actions = new List<SkillAction>
            {
                new SkillAction { type = "push_enemy", time = 0.02f, range = 2.3f, power = 6f },
            },
        };

        static SkillData DebugPull() => new SkillData
        {
            slot = SkillSlot.AttackC,
            skill_name = "DBG 引き寄せ",
            description = "重力で相手を引く",
            element = Element.Dark,
            risk_level = RiskLevel.Low,
            parameters = Params(0f, 3.0f, 0.02f, 0.08f, 0.22f, 1, 0f, 0f, 0f),
            actions = new List<SkillAction>
            {
                new SkillAction { type = "pull_enemy", time = 0.02f, range = 3.0f, power = 5.5f },
            },
        };

        static SkillData DebugBuff(string status)
        {
            string label = status switch
            {
                "speed" => "速度",
                "jump" => "ジャンプ",
                "transparent" => "透明",
                _ => "攻撃",
            };
            float power = status switch
            {
                "jump" => 1.35f,
                "transparent" => 1f,
                _ => 1.45f,
            };
            return new SkillData
            {
                slot = SkillSlot.AttackC,
                skill_name = "DBG " + label + "バフ",
                description = "自己強化",
                element = Element.Wind,
                risk_level = RiskLevel.Low,
                parameters = Params(0f, 0.5f, 0f, 0.02f, 0.14f, 1, 0f, 0f, 0f),
                actions = new List<SkillAction>
                {
                    new SkillAction { type = "buff_self", time = 0f, status = status, power = power, duration = status == "transparent" ? 1.0f : 4.0f },
                },
            };
        }

        static SkillData DebugCommandThrow() => new SkillData
        {
            slot = SkillSlot.AttackC,
            skill_name = "DBG コマンド投げ",
            description = "掴んで投げる（ガード貫通）",
            element = Element.Physical,
            risk_level = RiskLevel.High,
            parameters = Params(8f, 1.8f, 0.05f, 0.1f, 0.3f, 1, 6f, 0.4f, 0f),
            actions = new List<SkillAction>
            {
                new SkillAction { type = "command_throw", time = 0.05f, range = 1.8f, size_y = 2.0f },
            },
        };

        static SkillData DebugBarrier() => new SkillData
        {
            slot = SkillSlot.AttackC,
            skill_name = "DBG バリア",
            description = "自己バフ：ダメージ吸収",
            element = Element.None,
            risk_level = RiskLevel.Low,
            parameters = Params(0f, 0.5f, 0f, 0.02f, 0.2f, 1, 0f, 0f, 0f),
            actions = new List<SkillAction>
            {
                new SkillAction { type = "barrier", time = 0f, power = 10f, duration = 3f },
            },
        };

        static SkillData DebugShockwave() => new SkillData
        {
            slot = SkillSlot.AttackC,
            skill_name = "DBG 衝撃波",
            description = "左右に広がる地面波",
            element = Element.Physical,
            risk_level = RiskLevel.Medium,
            parameters = Params(5f, 2.2f, 0.06f, 0.25f, 0.34f, 1, 4f, 0.1f, 0.6f),
            actions = new List<SkillAction>
            {
                new SkillAction { type = "shockwave", time = 0.06f, range = 2.2f, size_x = 2.0f, size_y = 0.8f, spawn_y = 0.3f },
            },
        };

        static SkillData DebugGravityWell() => new SkillData
        {
            slot = SkillSlot.AttackC,
            skill_name = "DBG 重力場",
            description = "範囲に引力を発生",
            element = Element.Dark,
            risk_level = RiskLevel.Medium,
            parameters = Params(0f, 3.5f, 0.05f, 0.1f, 0.28f, 1, 0f, 0f, 0f),
            actions = new List<SkillAction>
            {
                new SkillAction { type = "gravity_well", time = 0.05f, range = 3.5f, power = 18f, spawn_x = 2.5f, spawn_y = 1.0f, duration = 1.2f },
            },
        };

        static SkillData DebugLifesteal() => new SkillData
        {
            slot = SkillSlot.AttackA,
            skill_name = "DBG 吸収近接",
            description = "与ダメの一部を回復",
            element = Element.Dark,
            risk_level = RiskLevel.Medium,
            parameters = Params(6f, 1.1f, 0.04f, 0.14f, 0.26f, 1, 3f, 0.08f, 0.6f),
            actions = new List<SkillAction>
            {
                new SkillAction { type = "lifesteal", time = 0.04f, range = 1.1f, size_y = 1.1f, spawn_x = 0.9f, spawn_y = 0.55f, follow_owner = true, lifesteal_ratio = 0.3f },
            },
        };

        static SkillData DebugHealSelf() => new SkillData
        {
            slot = SkillSlot.AttackC,
            skill_name = "DBG 自己回復",
            description = "最大HPの5%回復",
            element = Element.None,
            risk_level = RiskLevel.Low,
            parameters = Params(0f, 0.5f, 0f, 0.02f, 0.3f, 1, 0f, 0f, 0f),
            actions = new List<SkillAction>
            {
                new SkillAction { type = "heal_self", time = 0f },
            },
        };
    }
}
