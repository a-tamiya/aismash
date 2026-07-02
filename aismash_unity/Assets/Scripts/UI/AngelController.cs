using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PromptFighters.AI;
using PromptFighters.Audio;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    // ボイスボール・コントローラー。
    // 一定間隔でスマッシュボール風のボイスボール（VoiceItem）を出現させ、耐久を0にして
    // 破壊した（取得した）プレイヤーに、スロー＋5秒の音声入力の権利を与える。
    // 音声内容を LLM が解釈してギミックを決定・適用する（Whisper/AIAngelClient/AngelGimmickApplier を流用）。
    // クラス名は既存参照（BattleManager/GameAudioManager/PreBattlePanel）維持のため踏襲。
    public class AngelController : MonoBehaviour
    {
        public static bool Enabled = true;

        // 取得〜録音〜効果適用のシーケンス実行中か。
        // 実況（CommentaryController）がこの間に喋ると、録音マイクに実況音声が混入したり
        // ボイスボールのTTSと重なったりするため、実況側が参照して発話を控える。
        public static bool SequenceBusy { get; private set; }
        public float recordSeconds  = 5f;
        public float spawnInterval  = 20f;  // アイテム出現間隔（秒）
        public float firstSpawnDelay = 8f;  // 試合開始から最初の出現まで
        public float slowScale      = 0.4f; // 録音中のスローモーション倍率

        bool          _busy;
        bool          _firstItemShown; // マッチで最初のボイスボール出現時に導入バナーを出す
        BattleManager _bm;
        AngelGimmickApplier _applier;
        Coroutine     _spawnRoutine;
        VoiceItem     _activeItem;
        float         _recordEndRealtime;
        bool          _listening;
        float         _baseFixedDelta;

        // 取得者の識別（1P=青/2P=赤/ボス=黒）。バナー・字幕の色分けに使う。
        Color  _acquirerColor = Color.white;
        string _acquirerTag   = "1P";

        // スロー演出オーバーレイ
        CanvasGroup     _slowGroup;
        UnityEngine.UI.Image _slowTint;

        // 上部バナー
        CanvasGroup     _bannerGroup;
        TextMeshProUGUI _titleLabel;
        TextMeshProUGUI _statusLabel;

        // 中央大表示（ギミック効果）
        CanvasGroup     _effectGroup;
        TextMeshProUGUI _effectLabel;
        Coroutine       _effectFade;

        // 下部字幕欄
        CanvasGroup     _subtitleGroup;
        TextMeshProUGUI _subtitleLabel;

        AudioSource _audioSource;

        void Awake()
        {
            _bm      = GetComponent<BattleManager>();
            _applier = gameObject.AddComponent<AngelGimmickApplier>();
            BuildUI();
        }

        public void StopVoice()
        {
            _audioSource?.Stop();
            if (_spawnRoutine != null) { StopCoroutine(_spawnRoutine); _spawnRoutine = null; }
            StopAllCoroutines();
            _spawnRoutine = null;
            if (_activeItem != null) { Destroy(_activeItem.gameObject); _activeItem = null; }
            RestoreTimeScale();
            ShowSlowOverlay(false);
            _busy = false; _listening = false; SequenceBusy = false;
            if (_bannerGroup != null)   _bannerGroup.alpha   = 0f;
            if (_subtitleGroup != null) _subtitleGroup.alpha = 0f;
            if (_effectGroup != null)   _effectGroup.alpha   = 0f;
        }

        void OnDestroy()
        {
            if (_audioSource != null) Destroy(_audioSource.gameObject);
        }

        void OnEnable()
        {
            if (_bm == null) return;
            _bm.OnBattleStart += OnBattleStart;
            _bm.OnBattleEnd   += OnBattleEnd;
        }

        void OnDisable()
        {
            if (_bm == null) return;
            _bm.OnBattleStart -= OnBattleStart;
            _bm.OnBattleEnd   -= OnBattleEnd;
        }

        void OnBattleStart()
        {
            if (!Enabled) return;
            // OnBattleStart はラウンドごと（カウントダウン明け）に発火する。障害物は BO3 を通して
            // 残したいので、マッチ最初のラウンド(=1)でのみ前マッチの残骸を一掃する。
            if (_bm == null || _bm.CurrentRound <= 1) { _applier?.ClearObstacles(); _firstItemShown = false; }
            if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
            _spawnRoutine = StartCoroutine(SpawnLoop());
        }

        void OnBattleEnd(int _)
        {
            if (_spawnRoutine != null) { StopCoroutine(_spawnRoutine); _spawnRoutine = null; }
            if (_activeItem != null) { Destroy(_activeItem.gameObject); _activeItem = null; }
            // BO3（マッチ）終了。地形・障害物はここまで残し、ここで破棄する。
            _applier?.ClearObstacles();
            RestoreTimeScale();
            ShowSlowOverlay(false);
            _busy = false; _listening = false; SequenceBusy = false;
        }

        // 一定間隔でアイテムを1個ずつ出現（同時に1個まで）。録音中は出さない。
        IEnumerator SpawnLoop()
        {
            yield return new WaitForSeconds(firstSpawnDelay);
            while (true)
            {
                if (Enabled && !_busy && _activeItem == null &&
                    _bm != null && _bm.Phase == BattlePhase.Fighting)
                {
                    SpawnItem();
                }
                yield return new WaitForSeconds(spawnInterval);
            }
        }

        void SpawnItem()
        {
            float halfW = _bm != null ? _bm.stageHalfWidth * 0.7f : 4.5f;
            float x = Random.Range(-halfW, halfW);
            float y = Random.Range(-0.4f, 2.6f); // 空中
            _activeItem = VoiceItem.Spawn(new Vector2(x, y), halfW, OnItemBroken);
            if (!_firstItemShown)
            {
                _firstItemShown = true;
                ShowBanner("[ ボイスボール出現！ ]", "壊してAIを味方につけろ！");
                if (_busy == false) StartCoroutine(HideBannerAfter(3.2f));
            }
            else
            {
                ShowBanner("[ ボイスボール出現！ ]", "攻撃して破壊すると…願いが叶う！");
                if (_busy == false) StartCoroutine(HideBannerAfter(2.4f));
            }
        }

        IEnumerator HideBannerAfter(float sec)
        {
            yield return new WaitForSeconds(sec);
            if (!_busy) HideBanner();
        }

        // VoiceItem が破壊された＝取得された時のコールバック（breaker = 取得者）
        void OnItemBroken(Fighter breaker)
        {
            _activeItem = null;
            if (_busy) return; // 念のため二重起動防止
            StartCoroutine(AcquireSequence(breaker));
        }

        IEnumerator AcquireSequence(Fighter breaker)
        {
            _busy = true;
            SequenceBusy = true;

            // ギミックの適用対象を「取得者=自分 / 相手」で割り当てる。
            // 取得者は常に applyP1（acquirerSlot=player1=自分）、相手は applyP2（player2）。
            //  ・Versus: 相手は対戦相手のファイター。
            //  ・協力(対ボス、P1/P2 vs BOSS): プレイヤーが取得なら相手=ボス、ボスが取得なら相手=プレイヤー側。
            bool coop = _bm != null && _bm.Mode == BattleMode.CoopVsBoss;
            bool breakerIsBoss = coop && breaker != null && breaker == _bm.boss;
            Fighter applyP1, applyP2;
            string  acquirerSlot = "player1";
            if (coop)
            {
                if (breakerIsBoss)
                {
                    applyP1 = _bm.boss;            // 自分=ボス
                    applyP2 = OpponentPlayerForBoss(); // 相手=プレイヤー側
                }
                else
                {
                    applyP1 = breaker;            // 自分=取得プレイヤー
                    applyP2 = _bm.boss;           // 相手=ボス
                }
            }
            else
            {
                applyP1 = _bm?.fighter1;
                applyP2 = _bm?.fighter2;
                acquirerSlot = breaker != null && breaker == _bm?.fighter2 ? "player2" : "player1";
            }

            string acquirerName;
            if (breakerIsBoss) acquirerName = "BOSS";
            else if (breaker != null && breaker == _bm?.fighter2) acquirerName = _bm?.Character2?.characterName ?? "2P";
            else acquirerName = _bm?.Character1?.characterName ?? "1P";

            // 取得者の色・タグを決定（1P=青 / 2P=赤 / ボス=黒）
            _acquirerTag   = VoiceItem.AcquirerTag(breaker);
            _acquirerColor = VoiceItem.AcquirerColor(breaker);

            // CPU（AI操作・ボス含む）が取得した場合は音声入力を待たず、
            // 取得者にメリットのあるギミックを即決定して適用する。
            if (IsCpu(breaker))
            {
                ShowBanner($"[ {_acquirerTag}：{acquirerName} がボイスボール獲得！ ]", "AIが力を得た！");
                _titleLabel.color = _acquirerColor;
                GameAudioManager.Instance?.PlayGimmickBuff();
                yield return new WaitForSecondsRealtime(1.2f);

                var cg = BeneficialGimmick(acquirerSlot);
                ShowBanner("[ ボイスボールの効果 ]", cg.message);
                ShowSubtitle(cg.message);
                _applier.Acquirer = breaker;
                _applier.Apply(cg, applyP1, applyP2);
                ShowEffectCenter(BuildEffectText(cg));
                AITTSClient.Speak(this, cg.message, _audioSource,
                    onError: e => Debug.LogWarning("[VoiceItemTTS] " + e),
                    voice: AITTSClient.AngelVoice, volume: 2.0f);

                // 実況にもこの瞬間を拾わせる（効果内容つき）
                BattleLogger.Instance?.LogEvent($"ボイスボール効果発動:{cg.message}");
                CommentaryController.NotifyMoment(
                    $"{_acquirerTag} {acquirerName}がボイスボールを獲得し、効果「{cg.message}」が発動した");

                yield return new WaitForSecondsRealtime(3f);
                HideBanner();
                HideSubtitle();
                _busy = false;
                SequenceBusy = false;
                yield break;
            }

            // 1. スローモーション開始（KO演出中は触らない）
            bool slowed = false;
            if (_bm == null || !_bm.IsKoSlowActive)
            {
                _baseFixedDelta = Time.fixedDeltaTime;
                Time.timeScale = slowScale;
                // 物理ステップを実時間で同頻度に保ち、スロー中もFPS（滑らかさ）を落とさない
                Time.fixedDeltaTime = _baseFixedDelta * slowScale;
                slowed = true;
                ShowSlowOverlay(true);
            }

            ShowBanner($"[ {_acquirerTag}：{acquirerName} がボイスボール獲得！ ]", "願いを話して！（マイクに向かって）");
            _titleLabel.color = _acquirerColor;
            GameAudioManager.Instance?.PlayGimmickBuff();

            // 2. 5秒録音（実時間。スローでも尺は変わらない）
            string transcribed = null;
            bool   recordDone  = false;
            _listening = false;
            _recordEndRealtime = Time.unscaledTime + recordSeconds;
            WhisperClient.RecordAndTranscribe(this, recordSeconds,
                text => { transcribed = text; recordDone = true; },
                err  => { Debug.LogWarning("[VoiceItem] Whisper: " + err); recordDone = true; },
                onRecordingStart: () =>
                {
                    _listening = true;
                    _recordEndRealtime = Time.unscaledTime + recordSeconds;
                    // 声が入るまでは認識結果を出さず「認識中…」とだけ表示する
                    // （無音をWhisperに送るとハルシネーションが出るため）。
                    ShowSubtitle("認識中…");
                },
                // 録音中、実際に声が入った分だけ認識途中経過をリアルタイム表示する（確定前のプレビュー）。
                onPartial: t => ShowSubtitle($"認識中: 「{t}」"));

            while (!recordDone)
            {
                if (_listening)
                {
                    float remaining = Mathf.Max(0f, _recordEndRealtime - Time.unscaledTime);
                    ShowListeningBanner(remaining);
                }
                else
                {
                    ShowBanner($"[ {acquirerName} がボイスボール獲得！ ]", "マイク準備中...");
                }
                yield return null;
            }
            _listening = false;

            // 3. スロー解除（解析・適用は通常速度で）
            if (slowed) { RestoreTimeScale(); ShowSlowOverlay(false); }

            // 4. ギミック決定
            GimmickData gimmick = null;
            bool        hadVoice = !string.IsNullOrEmpty(transcribed);

            // 音声が何と認識されたかを画面に表示する（プレイヤーが認識結果を文字で確認できる）
            if (hadVoice)
            {
                ShowBanner($"[ {_acquirerTag} の声 ]", $"「{transcribed}」");
                _titleLabel.color = _acquirerColor;
                ShowSubtitle($"認識結果: 「{transcribed}」");
                yield return new WaitForSecondsRealtime(1.4f);
            }
            else
            {
                ShowSubtitle("うまく聞き取れなかった…ランダム効果！");
                yield return new WaitForSecondsRealtime(1.0f);
            }

            if (hadVoice)
            {
                // 願いが取れた → LLM が言葉を解釈して忠実に叶える（取得者を文脈に渡す）
                ShowBanner("[ ボイスボールの効果 ]", "願いを解析中...");
                var battleState = BuildBattleState();
                bool decided = false;
                AIAngelClient.DecideGimmick(this, transcribed, battleState,
                    data => { gimmick = data; decided = true; },
                    err  => { Debug.LogWarning("[VoiceItem] " + err); decided = true; },
                    acquirerSlot: acquirerSlot);
                float timeout = 18f;
                while (!decided && timeout > 0f) { timeout -= Time.unscaledDeltaTime; yield return null; }
            }

            // 5. 音声なし/解析失敗時は、毎回ばらける完全ランダムなギミック（APIは呼ばない）
            if (gimmick == null)
                gimmick = RandomGimmick(acquirerSlot, acquirerName);

            // 6. 適用 + 表示 + TTS
            ShowBanner("[ ボイスボールの効果 ]", gimmick.message);
            ShowSubtitle(gimmick.message);
            _applier.Acquirer = breaker; // hp_set 等「発動者に跳ね返る」ギミック用
            _applier.Apply(gimmick, applyP1, applyP2);
            ShowEffectCenter(BuildEffectText(gimmick));
            AITTSClient.Speak(this, gimmick.message, _audioSource,
                onError: e => Debug.LogWarning("[VoiceItemTTS] " + e),
                voice: AITTSClient.AngelVoice,
                volume: 2.0f);

            // 実況にもこの瞬間を拾わせる（願いの内容＋効果つき）
            BattleLogger.Instance?.LogEvent($"ボイスボール効果発動:{gimmick.message}");
            CommentaryController.NotifyMoment(hadVoice
                ? $"{_acquirerTag} {acquirerName}がボイスボールに願い「{transcribed}」を唱え、効果「{gimmick.message}」が発動した"
                : $"{_acquirerTag} {acquirerName}が獲得したボイスボールから効果「{gimmick.message}」が発動した");

            yield return new WaitForSecondsRealtime(3.5f);
            HideBanner();
            HideSubtitle();

            _busy = false;
            SequenceBusy = false;
        }

        void RestoreTimeScale()
        {
            // KO演出が時間制御中なら触らない（KO側が最後に1へ戻す）
            if (_bm != null && _bm.IsKoSlowActive) return;
            Time.timeScale = 1f;
            if (_baseFixedDelta > 0f) Time.fixedDeltaTime = _baseFixedDelta;
        }

        void ShowSlowOverlay(bool on)
        {
            if (_slowGroup != null) _slowGroup.alpha = on ? 1f : 0f;
        }

        void Update()
        {
            // スロー演出中の視覚効果（実時間で脈動。スローでもヌルヌル動く）
            if (_slowGroup == null || _slowGroup.alpha <= 0f) return;
            float p = (Mathf.Sin(Time.unscaledTime * 4f) + 1f) * 0.5f;
            if (_slowTint != null)
                _slowTint.color = new Color(0.25f, 0.6f, 1f, Mathf.Lerp(0.08f, 0.16f, p));
        }

        // 音声なし時の完全ランダムギミック。バフ・デバフ・特殊・地形を幅広く、
        // ターゲットも様々（取得者/相手/両者/弱い方 等）に散らして、毎回違う展開にする。
        // who: "self"=取得者 / "opp"=相手 / それ以外はそのまま target に入れる。
        // 取得者がCPU（AI操作・ボス含む）か。FighterAIが有効なら人間ではない。
        static bool IsCpu(Fighter f)
        {
            if (f == null) return false;
            var ai = f.GetComponent<FighterAI>();
            return ai != null && ai.enabled;
        }

        // 協力モードでボスが取得した時の「相手」プレイヤー。生存中の強い方を代表に選ぶ。
        Fighter OpponentPlayerForBoss()
        {
            var f1 = _bm?.fighter1;
            var f2 = _bm?.fighter2;
            bool f1ok = f1 != null && f1.State != FighterState.Dead && !f1.IsDowned;
            bool f2ok = f2 != null && f2.State != FighterState.Dead && !f2.IsDowned;
            if (f1ok && f2ok) return f1.CurrentHP >= f2.CurrentHP ? f1 : f2;
            if (f1ok) return f1;
            if (f2ok) return f2;
            return f1 ?? f2;
        }

        // CPU取得時に使う「取得者にメリットのある」ギミック（自分強化 or 相手弱体のみ）。
        static GimmickData BeneficialGimmick(string acquirerSlot)
        {
            string oppSlot = acquirerSlot == "player2" ? "player1" : "player2";
            (string g, float v, float d, string who, string msg)[] picks =
            {
                ("hp_recover",  0.30f, 0f, "self", "HPが回復した！"),
                ("speed_boost", 1.40f, 8f, "self", "スピードアップ！"),
                ("jump_boost",  1.40f, 8f, "self", "ジャンプ強化！"),
                ("damage_boost",1.50f, 8f, "self", "パワーアップ！"),
                ("invincible",  0f,    4f, "self", "無敵化！"),
                ("guard_fill",  0f,    0f, "self", "ガード全回復！"),
                ("reflect",     0f,    6f, "self", "ダメージ反射！"),
                ("size_up",     1.35f, 8f, "self", "巨大化！"),
                ("speed_down",  0.60f, 8f, "opp",  "相手が鈍足に！"),
                ("damage_down", 0.60f, 8f, "opp",  "相手のパワーダウン！"),
                ("freeze",      0f,    2f, "opp",  "相手を氷漬け！"),
                ("burn",        0f,    6f, "opp",  "相手に火がついた！"),
                ("guard_break", 0f,    0f, "opp",  "相手のガード破壊！"),
                ("launch",      3.0f,  0f, "opp",  "相手を吹き飛ばし！"),
                ("hp_drain",    0.20f, 0f, "opp",  "相手のHPを削った！"),
            };
            var p = picks[Random.Range(0, picks.Length)];
            return new GimmickData
            {
                gimmick  = p.g,
                target   = p.who == "self" ? acquirerSlot : oppSlot,
                value    = p.v,
                duration = p.d,
                message  = p.msg,
            };
        }

        static GimmickData RandomGimmick(string acquirerSlot, string acquirerName)
        {
            string oppSlot = acquirerSlot == "player2" ? "player1" : "player2";

            (string g, float v, float d, string who, string msg)[] picks =
            {
                // 取得者バフ
                ("hp_recover",   0.30f, 0f, "self", "HPが回復した！"),
                ("speed_boost",  1.40f, 8f, "self", "スピードアップ！"),
                ("jump_boost",   1.40f, 8f, "self", "ジャンプ強化！"),
                ("damage_boost", 1.50f, 8f, "self", "パワーアップ！"),
                ("invincible",   0f,    4f, "self", "無敵化！"),
                ("guard_fill",   0f,    0f, "self", "ガード全回復！"),
                ("gravity_down", 0.45f, 8f, "self", "ふわふわ浮遊！"),
                ("reflect",      0f,    6f, "self", "ダメージ反射！"),
                ("size_up",      1.40f, 8f, "self", "巨大化！"),
                // 相手デバフ
                ("speed_down",   0.60f, 8f, "opp",  "相手が鈍足に！"),
                ("damage_down",  0.60f, 8f, "opp",  "相手のパワーダウン！"),
                ("freeze",       0f,    2f, "opp",  "相手が氷漬け！"),
                ("burn",         0f,    6f, "opp",  "相手に火がついた！"),
                ("chaos",        0f,    6f, "opp",  "相手の操作が混乱！"),
                ("guard_break",  0f,    0f, "opp",  "相手のガード破壊！"),
                ("launch",       3.0f,  0f, "opp",  "相手を吹き飛ばし！"),
                ("gravity_up",   3.0f,  8f, "opp",  "相手が重力に潰される！"),
                ("size_down",    0.65f, 8f, "opp",  "相手が縮小！"),
                ("hp_drain",     0.20f, 0f, "opp",  "相手のHPを削った！"),
                // カオス・特殊・地形（誰に転ぶか分からない）
                ("hp_swap",      0f,    0f, "both", "HPを入れ替え！"),
                ("position_swap",0f,    0f, "both", "位置を入れ替え！"),
                ("teleport",     0f,    0f, "random", "ランダムワープ！"),
                ("slow",         0.5f,  6f, "random", "スロー発生！"),
                ("obstacle_rain",6f,    0f, "both", "障害物の雨！"),
                ("obstacle_bounce",0f,  0f, "both", "バウンスパッド出現！"),
                ("obstacle_platform",3f,0f, "both", "足場が出現！"),
                ("obstacle_tilt",3f,    0f, "both", "斜め足場が出現！"),
            };
            var p = picks[Random.Range(0, picks.Length)];
            string target = p.who switch
            {
                "self" => acquirerSlot,
                "opp"  => oppSlot,
                _      => p.who, // both / random / weaker / stronger 等
            };
            return new GimmickData
            {
                gimmick  = p.g,
                target   = target,
                value    = p.v,
                duration = p.d,
                message  = p.msg,
            };
        }

        static string BuildEffectText(GimmickData g)
        {
            string line1 = $"{TargetLabel(g.target)} {EffectLabel(g.gimmick)}";
            if (!string.IsNullOrEmpty(g.gimmick2))
                line1 += $"\n{TargetLabel(g.target2)} {EffectLabel(g.gimmick2)}";
            return line1;
        }

        static string TargetLabel(string t) => t switch
        {
            "player1"  => "P1",
            "player2"  => "P2",
            "both"     => "両者",
            "weaker"   => "弱い方",
            "stronger" => "強い方",
            _          => t,
        };

        static string EffectLabel(string g) => g switch
        {
            "hp_recover"   => "HP 回復",
            "hp_full"      => "HP 全回復！",
            "hp_drain"     => "HP 削減",
            "hp_swap"      => "HP 入れ替え！",
            "speed_boost"  => "スピード UP ↑",
            "speed_down"   => "スピード DOWN ↓",
            "jump_boost"   => "ジャンプ UP ↑",
            "jump_down"    => "ジャンプ DOWN ↓",
            "damage_boost" => "パワー UP ↑",
            "damage_down"  => "パワー DOWN ↓",
            "transparent"  => "無敵化 ✦",
            "invincible"   => "無敵化 ✦",
            "chaos"        => "操作混乱 ！",
            "freeze"       => "行動不能 ！",
            "burn"         => "バーン状態",
            "guard_break"  => "ガード破壊 ！",
            "gravity_up"   => "重力増加 ↓↓",
            "gravity_down" => "重力低下 ↑↑",
            "size_up"      => "巨大化",
            "size_down"    => "縮小化",
            "obstacle"          => "足場出現 ！",
            "obstacle_platform" => "足場出現 ！",
            "obstacle_wall"     => "壁出現 ！",
            "obstacle_bounce"   => "バウンスパッド ！",
            "obstacle_rain"     => "障害物の雨 ！",
            "obstacle_tilt"  => "斜め足場出現 ！",
            "teleport"       => "瞬間移動 ！",
            "position_swap"  => "位置入れ替え ！",
            "launch"         => "吹き飛ばし ！",
            "slow"           => "スロー状態",
            "reflect"        => "ダメージ反射 ✦",
            "hp_set"         => "HP強制変更 ！",
            "guard_fill"     => "ガード全回復",
            "wind"           => "強風 〜〜",
            "floor_lava"     => "床が溶岩 ！",
            "guard_disable"  => "ガード不可 ！",
            "skill_seal"     => "技封印 ！",
            "super_knockback" => "ふっとび増 ！",
            "hp_equal"       => "HP平均化 ！",
            "hp_share"       => "HP共有 ！",
            "counter_gimmick" => "カウンター ✦",
            "ground_bounce"  => "跳ねる床 ！",
            "obstacle_moving" => "動く足場 ！",
            "clear_obstacles" => "障害物消去 ！",
            _                => g,
        };

        void ShowEffectCenter(string text)
        {
            _effectLabel.text = text;
            if (_effectFade != null) StopCoroutine(_effectFade);
            _effectFade = StartCoroutine(EffectFadeRoutine());
        }

        IEnumerator EffectFadeRoutine()
        {
            _effectGroup.alpha = 1f;
            yield return new WaitForSecondsRealtime(3f);
            float t = 0f;
            while (t < 0.6f) { t += Time.unscaledDeltaTime; _effectGroup.alpha = 1f - t / 0.6f; yield return null; }
            _effectGroup.alpha = 0f;
        }

        CommentaryBattleState BuildBattleState()
        {
            var f1     = _bm?.fighter1;
            var f2     = _bm?.fighter2;
            var logger = BattleLogger.Instance;
            return new CommentaryBattleState
            {
                player1Name    = _bm?.Character1?.characterName ?? "1P",
                player1HpRatio = f1 != null ? f1.CurrentHP / f1.maxHP : 0f,
                player2Name    = _bm?.Character2?.characterName ?? "2P",
                player2HpRatio = f2 != null ? f2.CurrentHP / f2.maxHP : 0f,
                timeRemaining  = _bm?.TimeRemaining ?? 0f,
                mostUsedSkillP1 = logger?.P1.MostUsedSkillName() ?? "",
                mostUsedSkillP2 = logger?.P2.MostUsedSkillName() ?? "",
                totalDamageP1   = logger?.P1.totalDamageDealt ?? 0f,
                totalDamageP2   = logger?.P2.totalDamageDealt ?? 0f,
                recentEvents    = logger?.RecentEventsSummary() ?? "",
                lastSkillP1     = logger?.P1.lastSkillName ?? "---",
                lastSkillP2     = logger?.P2.lastSkillName ?? "---",
                hitStreakP1     = logger?.P1.hitStreak ?? 0,
                hitStreakP2     = logger?.P2.hitStreak ?? 0,
                recentHitsP1    = logger?.P1RecentHits ?? 0,
                recentHitsP2    = logger?.P2RecentHits ?? 0,
                guardBreaksP1   = logger?.P1.guardBreaksDealt ?? 0,
                guardBreaksP2   = logger?.P2.guardBreaksDealt ?? 0,
            };
        }

        void ShowBanner(string title, string status)
        {
            _titleLabel.text  = title;
            _statusLabel.text = status;
            _titleLabel.color = UITheme.Gold;
            _statusLabel.color = Color.white;
            _bannerGroup.alpha = 1f;
        }

        void ShowListeningBanner(float remaining)
        {
            _titleLabel.text = $"[ {_acquirerTag} 音声入力受付中 ]";
            _statusLabel.text = $"録音中: 願いを話してね！ 残り {remaining:0.0}秒";

            // 受付中の段階から取得者カラー（1P青/2P赤/ボス黒）で色分けする。
            // 「録音中」が分かるよう白方向へ軽くシマーさせるが、基本色は取得者カラーを保つ。
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
            _titleLabel.color  = Color.Lerp(_acquirerColor, Color.white, 0.15f + 0.2f * pulse);
            _statusLabel.color = Color.Lerp(Color.white, _acquirerColor, 0.3f);
            _bannerGroup.alpha = 1f;
        }

        void HideBanner() => _bannerGroup.alpha = 0f;

        void ShowSubtitle(string text)
        {
            _subtitleLabel.text  = text;
            _subtitleGroup.alpha = 1f;
        }

        void HideSubtitle() => _subtitleGroup.alpha = 0f;

        void BuildUI()
        {
            var canvasGo = new GameObject("VoiceItemCanvas");
            DontDestroyOnLoad(canvasGo);

            var canvas = canvasGo.AddComponent<Canvas>();
            // ステージ背景(-10) と ファイター(0) の間に挿入
            if (Camera.main != null)
            {
                canvas.renderMode    = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera   = Camera.main;
                canvas.planeDistance = 5f;
                canvas.sortingOrder  = -5;
            }
            else
            {
                canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 55;
            }

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // ── 上部バナー ──
            var bannerGo = new GameObject("ItemBanner");
            bannerGo.transform.SetParent(canvasGo.transform, false);

            // HUDトップバー(高さ約102px)・協力時のボスHPバー(〜164px)と重ならない位置に置く
            var bannerRect = bannerGo.AddComponent<RectTransform>();
            bannerRect.anchorMin        = new Vector2(0f, 1f);
            bannerRect.anchorMax        = new Vector2(1f, 1f);
            bannerRect.pivot            = new Vector2(0.5f, 1f);
            bannerRect.anchoredPosition = new Vector2(0f, -172f);
            bannerRect.sizeDelta        = new Vector2(0f, 160f);

            _bannerGroup       = bannerGo.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;

            var bannerBg = UITheme.AddImage(bannerGo.transform, "BannerBg",
                new Color(PromptFighters.UI.UITheme.SteelDark.r, PromptFighters.UI.UITheme.SteelDark.g, PromptFighters.UI.UITheme.SteelDark.b, 0.66f),
                UITheme.VGradient);
            bannerBg.type = UnityEngine.UI.Image.Type.Simple;
            var bannerEdgeGo = new GameObject("BannerEdge");
            bannerEdgeGo.transform.SetParent(bannerGo.transform, false);
            var beRt = bannerEdgeGo.AddComponent<RectTransform>();
            beRt.anchorMin = new Vector2(0f, 0f); beRt.anchorMax = new Vector2(1f, 0f);
            beRt.pivot = new Vector2(0.5f, 0f); beRt.sizeDelta = new Vector2(0f, 5f);
            beRt.anchoredPosition = Vector2.zero;
            var beImg = bannerEdgeGo.AddComponent<UnityEngine.UI.Image>();
            beImg.color = UITheme.Gold; beImg.raycastTarget = false;
            bannerEdgeGo.AddComponent<UISkew>().slantPixels = 28f;

            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(bannerGo.transform, false);
            var titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(20f, 0f);
            titleRect.offsetMax = new Vector2(-20f, -8f);

            _titleLabel = titleGo.AddComponent<TextMeshProUGUI>();
            UITheme.Apply(_titleLabel, 72f, FontStyles.Bold | FontStyles.Italic);
            _titleLabel.color            = UITheme.Gold;
            _titleLabel.alignment        = TextAlignmentOptions.Center;
            _titleLabel.textWrappingMode = TextWrappingModes.NoWrap;

            var msgGo = new GameObject("Status");
            msgGo.transform.SetParent(bannerGo.transform, false);
            var msgRect = msgGo.AddComponent<RectTransform>();
            msgRect.anchorMin = Vector2.zero;
            msgRect.anchorMax = new Vector2(1f, 0.5f);
            msgRect.offsetMin = new Vector2(20f, 4f);
            msgRect.offsetMax = new Vector2(-20f, 0f);

            _statusLabel = msgGo.AddComponent<TextMeshProUGUI>();
            UITheme.Apply(_statusLabel, 44f);
            _statusLabel.color     = Color.white;
            _statusLabel.alignment = TextAlignmentOptions.Center;

            // ── 中央大表示（ギミック効果）──
            var effectGo = new GameObject("EffectDisplay");
            effectGo.transform.SetParent(canvasGo.transform, false);

            var effectRect = effectGo.AddComponent<RectTransform>();
            effectRect.anchorMin        = new Vector2(0f, 0.5f);
            effectRect.anchorMax        = new Vector2(1f, 0.5f);
            effectRect.pivot            = new Vector2(0.5f, 0.5f);
            effectRect.anchoredPosition = Vector2.zero;
            effectRect.sizeDelta        = new Vector2(0f, 200f);

            _effectGroup       = effectGo.AddComponent<CanvasGroup>();
            _effectGroup.alpha = 0f;

            var effectTextGo = new GameObject("EffectText");
            effectTextGo.transform.SetParent(effectGo.transform, false);
            var effectTextRect = effectTextGo.AddComponent<RectTransform>();
            effectTextRect.anchorMin = Vector2.zero;
            effectTextRect.anchorMax = Vector2.one;
            effectTextRect.offsetMin = new Vector2(16f, 8f);
            effectTextRect.offsetMax = new Vector2(-16f, -8f);

            _effectLabel = effectTextGo.AddComponent<TextMeshProUGUI>();
            UITheme.Apply(_effectLabel, 88f, FontStyles.Bold | FontStyles.Italic);
            _effectLabel.color     = UITheme.Gold;
            _effectLabel.alignment = TextAlignmentOptions.Center;
            _effectLabel.enableVertexGradient = true;
            _effectLabel.colorGradient = new VertexGradient(
                new Color(1f, 0.95f, 0.55f), new Color(1f, 0.95f, 0.55f),
                UITheme.Gold, UITheme.Gold);

            // ── 下部字幕欄 ──
            var subGo = new GameObject("ItemSubtitle");
            subGo.transform.SetParent(canvasGo.transform, false);

            var subRect = subGo.AddComponent<RectTransform>();
            subRect.anchorMin        = new Vector2(0f, 0f);
            subRect.anchorMax        = new Vector2(1f, 0f);
            subRect.pivot            = new Vector2(0.5f, 0f);
            subRect.anchoredPosition = new Vector2(0f, 100f);
            subRect.sizeDelta        = new Vector2(0f, 90f);

            _subtitleGroup       = subGo.AddComponent<CanvasGroup>();
            _subtitleGroup.alpha = 0f;

            var subBg = subGo.AddComponent<UnityEngine.UI.Image>();
            subBg.sprite = UITheme.VGradient;
            subBg.type = UnityEngine.UI.Image.Type.Simple;
            subBg.color = new Color(PromptFighters.UI.UITheme.SteelDark.r, PromptFighters.UI.UITheme.SteelDark.g, PromptFighters.UI.UITheme.SteelDark.b, 0.62f);

            var subAccentGo = new GameObject("SubAccent");
            subAccentGo.transform.SetParent(subGo.transform, false);
            var saRt = subAccentGo.AddComponent<RectTransform>();
            saRt.anchorMin = new Vector2(0f, 1f); saRt.anchorMax = new Vector2(1f, 1f);
            saRt.pivot = new Vector2(0.5f, 1f); saRt.sizeDelta = new Vector2(0f, 4f);
            saRt.anchoredPosition = Vector2.zero;
            var saImg = subAccentGo.AddComponent<UnityEngine.UI.Image>();
            saImg.color = UITheme.Gold; saImg.raycastTarget = false;
            subAccentGo.AddComponent<UISkew>().slantPixels = 24f;

            var subTextGo = new GameObject("SubtitleText");
            subTextGo.transform.SetParent(subGo.transform, false);
            var subTextRect = subTextGo.AddComponent<RectTransform>();
            subTextRect.anchorMin = Vector2.zero;
            subTextRect.anchorMax = Vector2.one;
            subTextRect.offsetMin = new Vector2(24f, 8f);
            subTextRect.offsetMax = new Vector2(-24f, -8f);

            _subtitleLabel = subTextGo.AddComponent<TextMeshProUGUI>();
            UITheme.Apply(_subtitleLabel, 40f, FontStyles.Bold | FontStyles.Italic);
            _subtitleLabel.color            = UITheme.Gold;
            _subtitleLabel.alignment        = TextAlignmentOptions.Center;
            _subtitleLabel.textWrappingMode = TextWrappingModes.Normal;
            _subtitleLabel.overflowMode     = TextOverflowModes.Truncate;

            BuildSlowOverlay();

            _audioSource             = canvasGo.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.volume      = 1f;
        }

        // スローモーション中だと一目で分かる演出（専用オーバーレイCanvas：青みティント＋中央"SLOW MOTION"）。
        // 最前面に出すため独立Canvasに置く（HUDのHPバー等は薄いティスト越しに見える）。
        void BuildSlowOverlay()
        {
            var canvasGo = new GameObject("SlowMoCanvas");
            DontDestroyOnLoad(canvasGo);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60; // 最前面寄り
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;

            var go = new GameObject("SlowOverlay");
            go.transform.SetParent(canvasGo.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _slowGroup = go.AddComponent<CanvasGroup>();
            _slowGroup.alpha = 0f;
            _slowGroup.blocksRaycasts = false;
            _slowGroup.interactable = false;

            // 全画面の青みティント（薄め。HUDは透けて見える）
            _slowTint = go.AddComponent<UnityEngine.UI.Image>();
            _slowTint.color = new Color(0.25f, 0.6f, 1f, 0.12f);
            _slowTint.raycastTarget = false;
        }
    }
}
