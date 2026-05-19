using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PromptFighters.AI;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    // 気まぐれ天使コントローラー。
    // 一定間隔で自動的に介入し、GPTがギミックを決定して適用する。
    // BattleManager の Awake で AddComponent される。
    public class AngelController : MonoBehaviour
    {
        public static bool Enabled = true;

        public float minInterval = 25f;
        public float maxInterval = 45f;

        enum AngelState { Idle, Processing, Displaying }

        AngelState    _state = AngelState.Idle;
        bool          _busy;
        BattleManager      _bm;
        AngelGimmickApplier _applier;
        Coroutine     _loopRoutine;

        // UI
        CanvasGroup       _group;
        TextMeshProUGUI   _angelLabel;
        TextMeshProUGUI   _statusLabel;
        AudioSource       _audioSource;

        void Awake()
        {
            _bm      = GetComponent<BattleManager>();
            _applier = gameObject.AddComponent<AngelGimmickApplier>();
            BuildUI();
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
            if (_loopRoutine != null) StopCoroutine(_loopRoutine);
            _loopRoutine = StartCoroutine(AngelLoop());
        }

        void OnBattleEnd(int _)
        {
            if (_loopRoutine != null) { StopCoroutine(_loopRoutine); _loopRoutine = null; }
        }

        IEnumerator AngelLoop()
        {
            yield return new WaitForSeconds(12f); // 試合開始直後は少し待つ

            while (true)
            {
                yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));
                if (!_busy && Enabled && _bm != null && _bm.Phase == BattlePhase.Fighting)
                    StartCoroutine(AngelSequence());
            }
        }

        IEnumerator AngelSequence()
        {
            _busy  = true;
            _state = AngelState.Processing;
            ShowStatus("天使が状況を見ています...");

            var battleState = BuildBattleState();
            GimmickData gimmick = null;
            bool        decided = false;

            AIAngelClient.DecideGimmick(this, "", battleState,
                data => { gimmick = data; decided = true; },
                err  => { Debug.LogWarning("[Angel] " + err); decided = true; });

            float timeout = 18f;
            while (!decided && timeout > 0f) { timeout -= Time.deltaTime; yield return null; }

            if (gimmick == null)
                gimmick = new GimmickData { gimmick = "hp_recover", target = "weaker", value = 0.15f, message = "ちょっと退屈だから…HP少しあげる♪" };

            _state = AngelState.Displaying;
            ShowAngelMessage(gimmick.message);

            _applier.Apply(gimmick, _bm?.fighter1, _bm?.fighter2);

            AITTSClient.Speak(this, gimmick.message, _audioSource,
                onError: err => Debug.LogWarning("[AngelTTS] " + err),
                voice: AITTSClient.AngelVoice);

            yield return new WaitForSeconds(4f);
            HidePanel();

            _busy  = false;
            _state = AngelState.Idle;
        }

        CommentaryBattleState BuildBattleState()
        {
            var f1 = _bm?.fighter1;
            var f2 = _bm?.fighter2;
            return new CommentaryBattleState
            {
                player1Name    = _bm?.Character1?.characterName ?? "1P",
                player1HpRatio = f1 != null ? f1.CurrentHP / f1.maxHP : 0f,
                player2Name    = _bm?.Character2?.characterName ?? "2P",
                player2HpRatio = f2 != null ? f2.CurrentHP / f2.maxHP : 0f,
                timeRemaining  = _bm?.TimeRemaining ?? 0f,
            };
        }

        void ShowStatus(string text)
        {
            _angelLabel.text  = "[ 気まぐれ天使 ]";
            _statusLabel.text = text;
            _group.alpha = 1f;
        }

        void ShowAngelMessage(string message)
        {
            _angelLabel.text  = "[ 気まぐれ天使 ]";
            _statusLabel.text = message;
            _group.alpha = 1f;
        }

        void HidePanel() => _group.alpha = 0f;

        void BuildUI()
        {
            var canvasGo = new GameObject("AngelCanvas");
            DontDestroyOnLoad(canvasGo);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 55;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // 右下にパネルを配置
            var panelGo   = new GameObject("Panel");
            panelGo.transform.SetParent(canvasGo.transform, false);

            var panelRect = panelGo.AddComponent<RectTransform>();
            panelRect.anchorMin        = new Vector2(1f, 0f);
            panelRect.anchorMax        = new Vector2(1f, 0f);
            panelRect.pivot            = new Vector2(1f, 0f);
            panelRect.anchoredPosition = new Vector2(-16f, 70f);
            panelRect.sizeDelta        = new Vector2(320f, 80f);

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.55f, 0.18f, 0.72f, 0.85f);

            _group       = panelGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(panelGo.transform, false);
            var titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.55f);
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(8f, 0f);
            titleRect.offsetMax = new Vector2(-8f, -4f);

            _angelLabel = titleGo.AddComponent<TextMeshProUGUI>();
            UITheme.Apply(_angelLabel, 14f, FontStyles.Bold);
            _angelLabel.color     = new Color(1f, 0.9f, 0.5f);
            _angelLabel.alignment = TextAlignmentOptions.Center;

            var msgGo = new GameObject("Message");
            msgGo.transform.SetParent(panelGo.transform, false);
            var msgRect = msgGo.AddComponent<RectTransform>();
            msgRect.anchorMin = Vector2.zero;
            msgRect.anchorMax = new Vector2(1f, 0.55f);
            msgRect.offsetMin = new Vector2(8f, 4f);
            msgRect.offsetMax = new Vector2(-8f, 0f);

            _statusLabel = msgGo.AddComponent<TextMeshProUGUI>();
            UITheme.Apply(_statusLabel, 13f);
            _statusLabel.color     = Color.white;
            _statusLabel.alignment = TextAlignmentOptions.Center;

            _audioSource = canvasGo.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.volume = 1f;
        }
    }
}
