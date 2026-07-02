using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PromptFighters.AI;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    // 試合中にAI実況テロップ+音声を流す。
    // 固定間隔ではなくイベント駆動：ガードブレイク・連続ヒット・低HP・逆転・残り時間などの
    // 「熱い瞬間」を検知して即実況し、静かな時間だけランダム間隔のつなぎ実況を入れる。
    // BattleManager の Awake で AddComponent される。
    public class CommentaryController : MonoBehaviour
    {
        public static bool Enabled = true;

        public float minGapSeconds  = 8f;  // 実況同士の最短間隔（連発防止）
        public float idleIntervalMin = 13f; // 何も起きない時のつなぎ実況間隔（ランダム下限）
        public float idleIntervalMax = 24f; // 同・上限

        CanvasGroup _group;
        RectTransform _panelRect;
        TextMeshProUGUI _label;
        Image _livePlate;
        AudioSource _audioSource;
        Coroutine _loopRoutine;
        Coroutine _fadeRoutine;
        bool _isGenerating;
        BattleManager _bm;

        const float PanelH = 96f; // テロップ高さ（バフチップは110pxから積むため下を保つ）

        // ── イベント駆動実況の検知状態 ──
        float _lastCommentaryTime = -999f;
        float _nextIdleTime;
        int   _prevGb1, _prevGb2;               // ガードブレイク累計の前回値
        bool  _low1Announced, _low2Announced;   // 低HP実況を各1回に制限
        int   _lastStreakAnnounced1, _lastStreakAnnounced2;
        int   _prevLeader;                      // 0=互角 / 1=P1リード / 2=P2リード
        bool  _t30Announced, _t10Announced;
        readonly System.Collections.Generic.Queue<string> _recentLines =
            new System.Collections.Generic.Queue<string>(); // 直前の実況（繰り返し防止用）

        void Awake()
        {
            _bm = GetComponent<BattleManager>();
            BuildUI();
        }

        void Start()
        {
            if (_bm != null && _bm.Phase == BattlePhase.Fighting)
                OnBattleStart();
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

        void BuildUI()
        {
            var canvasGo = new GameObject("CommentaryCanvas");
            DontDestroyOnLoad(canvasGo);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            // 他のUIと同じ基準解像度でスケールさせる（未設定だと解像度によって文字サイズが変わる）
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var panelGo   = new GameObject("Panel");
            panelGo.transform.SetParent(canvasGo.transform, false);

            _panelRect = panelGo.AddComponent<RectTransform>();
            _panelRect.anchorMin = new Vector2(0f, 0f);
            _panelRect.anchorMax = new Vector2(1f, 0f);
            _panelRect.pivot     = new Vector2(0.5f, 0f);
            _panelRect.anchoredPosition = Vector2.zero;
            _panelRect.sizeDelta = new Vector2(0f, PanelH);

            var bg = panelGo.AddComponent<Image>();
            bg.sprite = UITheme.VGradient;
            bg.type   = Image.Type.Simple;
            bg.color  = new Color(0.02f, 0.025f, 0.045f, 0.92f);
            bg.raycastTarget = false;

            // 上端ゴールドアクセントライン（斜め）
            var accentGo = new GameObject("Accent");
            accentGo.transform.SetParent(panelGo.transform, false);
            var accRect = accentGo.AddComponent<RectTransform>();
            accRect.anchorMin = new Vector2(0f, 1f);
            accRect.anchorMax = new Vector2(1f, 1f);
            accRect.pivot     = new Vector2(0.5f, 1f);
            accRect.sizeDelta = new Vector2(0f, 3f);
            accRect.anchoredPosition = Vector2.zero;
            var accImg = accentGo.AddComponent<Image>();
            accImg.color = UITheme.Gold;
            accImg.raycastTarget = false;
            accentGo.AddComponent<UISkew>().slantPixels = 24f;

            _group       = panelGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            // 左の「● LIVE 実況」プレート（放送テロップ風・赤の斜めプレート）
            var plateGo = new GameObject("LivePlate");
            plateGo.transform.SetParent(panelGo.transform, false);
            var plateRect = plateGo.AddComponent<RectTransform>();
            plateRect.anchorMin = plateRect.anchorMax = new Vector2(0f, 0.5f);
            plateRect.pivot = new Vector2(0f, 0.5f);
            plateRect.anchoredPosition = new Vector2(20f, 0f);
            plateRect.sizeDelta = new Vector2(184f, 50f);
            _livePlate = plateGo.AddComponent<Image>();
            _livePlate.sprite = UITheme.VGradient;
            _livePlate.type = Image.Type.Simple;
            _livePlate.color = UITheme.Urgent;
            _livePlate.raycastTarget = false;
            UITheme.Skew(_livePlate, 12f);

            var plateLabelGo = new GameObject("LiveLabel");
            plateLabelGo.transform.SetParent(plateGo.transform, false);
            var plRect = plateLabelGo.AddComponent<RectTransform>();
            plRect.anchorMin = Vector2.zero;
            plRect.anchorMax = Vector2.one;
            plRect.offsetMin = plRect.offsetMax = Vector2.zero;
            var plateLabel = plateLabelGo.AddComponent<TextMeshProUGUI>();
            UITheme.Apply(plateLabel, 24f, FontStyles.Bold | FontStyles.Italic);
            plateLabel.text = "● LIVE 実況";
            plateLabel.color = Color.white;
            plateLabel.alignment = TextAlignmentOptions.Center;
            plateLabel.textWrappingMode = TextWrappingModes.NoWrap;
            plateLabel.raycastTarget = false;

            // 実況テキスト（プレートの右から左寄せで流す）
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(panelGo.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(228f, 6f);
            textRect.offsetMax = new Vector2(-24f, -8f);

            _label = textGo.AddComponent<TextMeshProUGUI>();
            UITheme.Apply(_label, 29f, FontStyles.Bold);
            _label.color     = Color.white;
            _label.alignment = TextAlignmentOptions.MidlineLeft;
            _label.textWrappingMode = TextWrappingModes.Normal;
            _label.overflowMode = TextOverflowModes.Truncate;
            _label.raycastTarget = false;

            _audioSource = canvasGo.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.volume = 1f;
        }

        void Update()
        {
            // 表示中はLIVEプレートを脈動させ、生放送感を出す
            if (_group == null || _group.alpha <= 0f || _livePlate == null) return;
            float p = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            _livePlate.color = Color.Lerp(new Color(0.68f, 0.10f, 0.08f, 1f), UITheme.Urgent, p);
        }

        public void StopVoice()
        {
            _audioSource?.Stop();
            if (_loopRoutine != null) { StopCoroutine(_loopRoutine); _loopRoutine = null; }
            if (_fadeRoutine != null) { StopCoroutine(_fadeRoutine); _fadeRoutine = null; }
            if (_group != null) _group.alpha = 0f;
            if (_panelRect != null) _panelRect.anchoredPosition = Vector2.zero; // スライド途中で止まった位置を戻す
        }

        void OnDestroy()
        {
            if (_audioSource != null) Destroy(_audioSource.gameObject);
        }

        void OnBattleStart()
        {
            if (!Enabled) return;
            ResetTriggerStates();
            if (_loopRoutine != null) StopCoroutine(_loopRoutine);
            _loopRoutine = StartCoroutine(CommentaryLoop());
        }

        void OnBattleEnd(int _)
        {
            if (_loopRoutine != null) { StopCoroutine(_loopRoutine); _loopRoutine = null; }
        }

        void ResetTriggerStates()
        {
            _lastCommentaryTime = -999f;
            _prevGb1 = _prevGb2 = 0;
            _low1Announced = _low2Announced = false;
            _lastStreakAnnounced1 = _lastStreakAnnounced2 = 0;
            _prevLeader = 0;
            _t30Announced = _t10Announced = false;
            _recentLines.Clear();
        }

        IEnumerator CommentaryLoop()
        {
            yield return new WaitForSeconds(5f); // 試合開始直後は少し待つ
            ScheduleNextIdle();

            // 0.5秒ごとに試合状況を監視し、熱い瞬間が来たら即実況。
            // 何も起きない時はランダム間隔のつなぎ実況でテンポを保つ。
            while (true)
            {
                if (_bm != null && _bm.Phase == BattlePhase.Fighting && !_isGenerating
                    && Time.time - _lastCommentaryTime >= minGapSeconds)
                {
                    string focus = DetectHotMoment();
                    bool idleDue = Time.time >= _nextIdleTime;
                    if (focus != null || idleDue)
                    {
                        _lastCommentaryTime = Time.time;
                        StartCoroutine(TriggerCommentary(focus));
                        ScheduleNextIdle();
                    }
                }
                yield return new WaitForSeconds(0.5f);
            }
        }

        void ScheduleNextIdle()
        {
            _nextIdleTime = Time.time + Random.Range(idleIntervalMin, idleIntervalMax);
        }

        // 「今まさに実況すべき瞬間」を検知する。検知したら焦点の説明文を返す（消費式）。
        string DetectHotMoment()
        {
            var lg = BattleLogger.Instance;
            var f1 = _bm?.fighter1;
            var f2 = _bm?.fighter2;
            if (lg == null || f1 == null || f2 == null) return null;
            string n1 = _bm.Character1?.characterName ?? "1P";
            string n2 = _bm.Character2?.characterName ?? "2P";

            // ガードブレイクの瞬間
            if (lg.P1.guardBreaksDealt > _prevGb1)
            {
                _prevGb1 = lg.P1.guardBreaksDealt;
                return $"{n1}が{n2}のガードを叩き割った（ガードブレイク発生）";
            }
            if (lg.P2.guardBreaksDealt > _prevGb2)
            {
                _prevGb2 = lg.P2.guardBreaksDealt;
                return $"{n2}が{n1}のガードを叩き割った（ガードブレイク発生）";
            }

            // 連続ヒットのラッシュ（4hit以上、更新時のみ）
            if (lg.P1.hitStreak >= 4 && lg.P1.hitStreak > _lastStreakAnnounced1)
            {
                _lastStreakAnnounced1 = lg.P1.hitStreak;
                return $"{n1}が{lg.P1.hitStreak}連続ヒットの猛ラッシュ中";
            }
            if (lg.P2.hitStreak >= 4 && lg.P2.hitStreak > _lastStreakAnnounced2)
            {
                _lastStreakAnnounced2 = lg.P2.hitStreak;
                return $"{n2}が{lg.P2.hitStreak}連続ヒットの猛ラッシュ中";
            }

            // 低HPの土壇場（各プレイヤー1回だけ）
            float r1 = f1.maxHP > 0f ? f1.CurrentHP / f1.maxHP : 1f;
            float r2 = f2.maxHP > 0f ? f2.CurrentHP / f2.maxHP : 1f;
            if (!_low1Announced && r1 <= 0.25f && f1.State != FighterState.Dead)
            {
                _low1Announced = true;
                return $"{n1}のHPが残りわずか。あと数発で決着の土壇場";
            }
            if (!_low2Announced && r2 <= 0.25f && f2.State != FighterState.Dead)
            {
                _low2Announced = true;
                return $"{n2}のHPが残りわずか。あと数発で決着の土壇場";
            }

            // 形勢逆転（リードの入れ替わり）
            int leader = r1 - r2 > 0.10f ? 1 : (r2 - r1 > 0.10f ? 2 : 0);
            if (leader != 0)
            {
                bool flipped = _prevLeader != 0 && leader != _prevLeader;
                _prevLeader = leader;
                if (flipped)
                    return $"形勢逆転！{(leader == 1 ? n1 : n2)}がリードを奪い返した";
            }

            // 残り時間の節目
            float t = _bm.TimeRemaining;
            if (!_t30Announced && t <= 30f && t > 0f)
            {
                _t30Announced = true;
                return "残り30秒を切った終盤戦。時間切れならHP残量の多い方が勝つ";
            }
            if (!_t10Announced && t <= 10f && t > 0f)
            {
                _t10Announced = true;
                return "残り10秒！ラストスパート";
            }

            return null;
        }

        IEnumerator TriggerCommentary(string focus)
        {
            _isGenerating = true;

            if (_bm == null || _bm.Phase != BattlePhase.Fighting) { _isGenerating = false; yield break; }

            var state = BuildState();
            state.focusEvent = focus;
            state.avoidLines = _recentLines.Count > 0 ? string.Join(" / ", _recentLines) : null;
            string result = null;
            bool done = false;

            AICommentaryClient.Generate(this, state,
                text => { result = text; done = true; },
                err  => { Debug.LogWarning("[Commentary] " + err); done = true; });

            float timeout = 14f;
            while (!done && timeout > 0f) { timeout -= Time.deltaTime; yield return null; }

            if (string.IsNullOrEmpty(result))
                result = BuildFallbackCommentary(state);

            _recentLines.Enqueue(result);
            while (_recentLines.Count > 3) _recentLines.Dequeue();

            ShowText(result);
            bool ttsDone = false;
            AITTSClient.Speak(this, result, _audioSource,
                onComplete: () => ttsDone = true,
                onError: err => { Debug.LogWarning("[CommentaryTTS] " + err); ttsDone = true; },
                voice: AITTSClient.CommentaryVoice,
                speed: AITTSClient.CommentarySpeed,
                volume: 2.2f,
                instructions: AITTSClient.CommentaryInstructions);

            _isGenerating = false;
            StartCoroutine(FadeOutAfterTTS(() => ttsDone));
        }

        CommentaryBattleState BuildState()
        {
            var f1     = _bm.fighter1;
            var f2     = _bm.fighter2;
            var logger = BattleLogger.Instance;

            return new CommentaryBattleState
            {
                player1Name      = _bm.Character1?.characterName ?? "1P",
                player1HpRatio   = f1 != null ? f1.CurrentHP / f1.maxHP : 0f,
                player2Name      = _bm.Character2?.characterName ?? "2P",
                player2HpRatio   = f2 != null ? f2.CurrentHP / f2.maxHP : 0f,
                timeRemaining    = _bm.TimeRemaining,
                mostUsedSkillP1  = logger?.P1.MostUsedSkillName() ?? "",
                mostUsedSkillP2  = logger?.P2.MostUsedSkillName() ?? "",
                totalDamageP1    = logger?.P1.totalDamageDealt ?? 0f,
                totalDamageP2    = logger?.P2.totalDamageDealt ?? 0f,
                recentEvents     = logger?.RecentEventsSummary() ?? "",
                lastSkillP1      = logger?.P1.lastSkillName ?? "---",
                lastSkillP2      = logger?.P2.lastSkillName ?? "---",
                hitStreakP1      = logger?.P1.hitStreak ?? 0,
                hitStreakP2      = logger?.P2.hitStreak ?? 0,
                recentHitsP1     = logger?.P1RecentHits ?? 0,
                recentHitsP2     = logger?.P2RecentHits ?? 0,
                guardBreaksP1    = logger?.P1.guardBreaksDealt ?? 0,
                guardBreaksP2    = logger?.P2.guardBreaksDealt ?? 0,
            };
        }

        static string BuildFallbackCommentary(CommentaryBattleState s)
        {
            // 焦点イベントがあればそれをそのまま叫ぶ（API失敗時でも瞬間を外さない）
            if (!string.IsNullOrEmpty(s.focusEvent))
                return s.focusEvent + "！";

            float diff = s.player1HpRatio - s.player2HpRatio;
            if (Mathf.Abs(diff) < 0.12f)
            {
                string[] even =
                {
                    "両者一歩も引かない！次の一撃が勝負の分かれ目だ！",
                    "互角の攻防！会場のボルテージが上がっていく！",
                    "まだ分からない！この試合、どちらに転ぶか読めません！",
                };
                return even[Random.Range(0, even.Length)];
            }

            string lead  = diff > 0f ? s.player1Name : s.player2Name;
            string chase = diff > 0f ? s.player2Name : s.player1Name;
            string[] leadLines =
            {
                $"{lead}が試合の主導権を握っている！{chase}は反撃の糸口を掴みたい！",
                $"{lead}、圧巻の攻めだ！{chase}、ここが踏ん張りどころ！",
                $"{lead}がリードを広げる！{chase}に残された時間は多くないぞ！",
            };
            return leadLines[Random.Range(0, leadLines.Length)];
        }

        void ShowText(string text)
        {
            _label.text = text;
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeInAndHold());
        }

        IEnumerator FadeInAndHold()
        {
            // 下からのスライドイン＋フェードで登場させる
            float t = 0f;
            const float dur = 0.28f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                float ease = 1f - (1f - k) * (1f - k); // ease-out
                _group.alpha = k;
                if (_panelRect != null)
                    _panelRect.anchoredPosition = new Vector2(0f, Mathf.Lerp(-PanelH * 0.6f, 0f, ease));
                yield return null;
            }
            _group.alpha = 1f;
            if (_panelRect != null) _panelRect.anchoredPosition = Vector2.zero;
        }

        IEnumerator FadeOutAfterTTS(System.Func<bool> isDone)
        {
            float waited = 0f;
            while (!isDone() && waited < 25f) { waited += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1.5f);
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeOutOnly());
        }

        IEnumerator FadeOutOnly()
        {
            float t = 0f;
            const float dur = 0.5f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                _group.alpha = 1f - k;
                if (_panelRect != null)
                    _panelRect.anchoredPosition = new Vector2(0f, -PanelH * 0.6f * k * k);
                yield return null;
            }
            _group.alpha = 0f;
            if (_panelRect != null) _panelRect.anchoredPosition = Vector2.zero;
        }
    }
}
