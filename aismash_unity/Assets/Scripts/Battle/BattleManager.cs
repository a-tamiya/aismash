using UnityEngine;
using UnityEngine.InputSystem;
using PromptFighters.Battle.Skills;
using PromptFighters.Utils;
using PromptFighters.UI;
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

        public int P1RoundWins  { get; private set; }
        public int P2RoundWins  { get; private set; }
        public int CurrentRound { get; private set; } = 1;

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
        public event System.Action<int>          OnBattleEnd;        // 0=1P wins, 1=2P wins, -1=draw (マッチ終了)
        public event System.Action<int,int,int>  OnRoundEnd;         // winnerIdx, p1wins, p2wins
        public event System.Action<int>          OnRoundStart;       // round number
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

            _mainCam = Camera.main;
            if (_mainCam != null)
            {
                _defaultCamOrthoSize = _mainCam.orthographicSize;
                _defaultCamPos       = _mainCam.transform.position;
            }
        }

        void Start()
        {
            if (fighter1 != null) fighter1.OnDeath += () => HandleFighterDeath(1);
            if (fighter2 != null) fighter2.OnDeath += () => HandleFighterDeath(0);
            EnsureNameplate(fighter1, "P1", new Color(0.4f, 0.75f, 1f));
            EnsureNameplate(fighter2, "P2", new Color(1f, 0.55f, 0.35f));

            // 陣営を設定（フレンドリーファイア判定用）。1v1は別陣営にして互いに攻撃可能にする。
            if (fighter1 != null) fighter1.Team = FighterTeam.Players;
            if (fighter2 != null) fighter2.Team = FighterTeam.Enemies;

            // 相手参照をセット（つかみ・状態異常・AIコメント用）
            if (fighter1 != null && fighter2 != null)
            {
                fighter1.Opponent = fighter2;
                fighter2.Opponent = fighter1;
            }

            ApplyCpuControl();

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

        // CPU対戦トグルに応じて2P側を FighterInput / FighterAI で切り替える。
        void ApplyCpuControl()
        {
            if (fighter2 == null) return;
            var input = fighter2.GetComponent<FighterInput>();
            var ai    = fighter2.GetComponent<FighterAI>();
            if (FighterAI.Enabled)
            {
                if (ai == null) ai = fighter2.gameObject.AddComponent<FighterAI>();
                ai.enabled = true;
                ai.ApplyLevel();
                if (input != null) input.enabled = false;
            }
            else
            {
                if (ai != null) ai.enabled = false;
                if (input != null) input.enabled = true;
            }
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

            GetComponent<StagePlatformSpawner>()?.SpawnPlatforms();
            ApplyCharacters(data1, data2);
            ApplyCpuControl();

            Phase     = BattlePhase.Countdown;
            Countdown = countdownLength;
            OnCountdownChanged?.Invoke(Countdown);
        }

        public void StartTraining(CharacterData data1, CharacterData data2)
        {
            if (Phase != BattlePhase.Setup && Phase != BattlePhase.Training) return;

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
            Debug.Log("[Battle] FIGHT!");
        }

        void EndByTimeout()
        {
            if (fighter1 == null || fighter2 == null) { FinishRoundOrMatch(-1); return; }
            if      (fighter1.CurrentHP > fighter2.CurrentHP) FinishRoundOrMatch(0);
            else if (fighter2.CurrentHP > fighter1.CurrentHP) FinishRoundOrMatch(1);
            else                                               FinishRoundOrMatch(-1);
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

            StopAllCoroutines();
            _hitStopActive = false;
            _koSlowActive  = false;
            Time.timeScale = 1f;
            Phase = BattlePhase.Setup;
            P1RoundWins  = 0;
            P2RoundWins  = 0;
            CurrentRound = 1;
            fighter1?.ResetForBattle(fighter1SpawnPos, faceRight: true);
            fighter2?.ResetForBattle(fighter2SpawnPos, faceRight: false);
            ResetCameraZoom();
            OnReturnedToSetup?.Invoke();
        }

        // ── ヒットストップ（Feature A）────────────────────────────────
        bool _hitStopActive;

        public void TriggerHitStop(float duration, float timeScale = 0.05f)
        {
            if (_hitStopActive) return; // 重複防止
            StartCoroutine(HitStopCoroutine(duration, timeScale));
        }

        IEnumerator HitStopCoroutine(float duration, float timeScale)
        {
            _hitStopActive = true;
            Time.timeScale = timeScale;
            yield return new WaitForSecondsRealtime(duration);
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

        public void TriggerKOSlow(Vector3 koPosition)
        {
            if (_koSlowActive) return;
            StartCoroutine(KOSlowCoroutine(koPosition));
        }

        IEnumerator KOSlowCoroutine(Vector3 koPosition)
        {
            _koSlowActive  = true;
            _hitStopActive = true; // HitStopが上書きしないようにロック

            float slowDuration = 2.5f;
            float zoomDuration = 0.25f;
            float zoomInSize   = _defaultCamOrthoSize * 0.70f;

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
