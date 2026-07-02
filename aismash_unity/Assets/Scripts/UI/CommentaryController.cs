using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PromptFighters.AI;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    // 試合中に定期的にAI実況テロップ+音声を流す。
    // BattleManager の Awake で AddComponent される。
    public class CommentaryController : MonoBehaviour
    {
        public static bool Enabled = true;

        public float commentaryInterval = 17f; // 実況間隔（秒）

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
            if (_loopRoutine != null) StopCoroutine(_loopRoutine);
            _loopRoutine = StartCoroutine(CommentaryLoop());
        }

        void OnBattleEnd(int _)
        {
            if (_loopRoutine != null) { StopCoroutine(_loopRoutine); _loopRoutine = null; }
        }

        IEnumerator CommentaryLoop()
        {
            yield return new WaitForSeconds(8f); // 試合開始直後は少し待つ

            while (true)
            {
                if (!_isGenerating) StartCoroutine(TriggerCommentary());
                yield return new WaitForSeconds(commentaryInterval + Random.Range(-3f, 3f));
            }
        }

        IEnumerator TriggerCommentary()
        {
            _isGenerating = true;

            if (_bm == null || _bm.Phase != BattlePhase.Fighting) { _isGenerating = false; yield break; }

            var state = BuildState();
            string result = null;
            bool done = false;

            AICommentaryClient.Generate(this, state,
                text => { result = text; done = true; },
                err  => { Debug.LogWarning("[Commentary] " + err); done = true; });

            float timeout = 14f;
            while (!done && timeout > 0f) { timeout -= Time.deltaTime; yield return null; }

            if (string.IsNullOrEmpty(result))
                result = BuildFallbackCommentary(state);

            ShowText(result);
            bool ttsDone = false;
            AITTSClient.Speak(this, result, _audioSource,
                onComplete: () => ttsDone = true,
                onError: err => { Debug.LogWarning("[CommentaryTTS] " + err); ttsDone = true; },
                voice: AITTSClient.CommentaryVoice,
                speed: AITTSClient.CommentarySpeed,
                volume: 2.2f);

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
            if (!string.IsNullOrEmpty(s.recentEvents))
                return "試合が動いています！ " + s.recentEvents;

            float diff = s.player1HpRatio - s.player2HpRatio;
            if (Mathf.Abs(diff) < 0.12f)
                return "互角の展開です。次の一撃で流れが変わりそうです。";
            string lead = diff > 0f ? s.player1Name : s.player2Name;
            return lead + "がリードしています。相手は反撃のきっかけが欲しいところです。";
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
