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
        TextMeshProUGUI _label;
        AudioSource _audioSource;
        Coroutine _loopRoutine;
        Coroutine _fadeRoutine;
        bool _isGenerating;
        BattleManager _bm;

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
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var panelGo   = new GameObject("Panel");
            panelGo.transform.SetParent(canvasGo.transform, false);

            var panelRect = panelGo.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot     = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(0f, 100f);

            panelGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            _group       = panelGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(panelGo.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20f, 4f);
            textRect.offsetMax = new Vector2(-20f, -4f);

            _label = textGo.AddComponent<TextMeshProUGUI>();
            UITheme.Apply(_label, 26f, FontStyles.Bold);
            _label.color     = Color.white;
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.Normal;
            _label.overflowMode = TextOverflowModes.Truncate;

            _audioSource = canvasGo.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.volume = 1f;
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
            float t = 0f;
            while (t < 0.3f) { t += Time.deltaTime; _group.alpha = t / 0.3f; yield return null; }
            _group.alpha = 1f;
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
            while (t < 0.5f) { t += Time.deltaTime; _group.alpha = 1f - t / 0.5f; yield return null; }
            _group.alpha = 0f;
        }
    }
}
