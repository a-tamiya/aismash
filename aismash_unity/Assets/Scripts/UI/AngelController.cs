using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using PromptFighters.AI;
using PromptFighters.Battle;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;
#endif

namespace PromptFighters.UI
{
    // 気まぐれ天使コントローラー。
    // Tabキーで起動 → マイク録音 → Whisper → GPTギミック決定 → 天使セリフ表示 + ギミック適用。
    // BattleManager の Awake で AddComponent される。
    public class AngelController : MonoBehaviour
    {
        public static bool Enabled = true;

        public float recordSeconds = 3f;
        public float cooldownTime  = 30f;
        public string[] wakeWords = { "天使", "妖精", "エンジェル" };

        enum AngelState { Idle, Recording, Processing, Displaying }

        AngelState  _state       = AngelState.Idle;
        float       _cooldown;
        BattleManager      _bm;
        AngelGimmickApplier _applier;

        // UI
        CanvasGroup       _group;
        TextMeshProUGUI   _angelLabel;
        TextMeshProUGUI   _statusLabel;
        AudioSource       _audioSource;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        KeywordRecognizer _keywordRecognizer;
#endif

        void Awake()
        {
            _bm      = GetComponent<BattleManager>();
            _applier = gameObject.AddComponent<AngelGimmickApplier>();
            BuildUI();
        }

        void OnEnable()
        {
            StartWakeRecognizer();
        }

        void OnDisable()
        {
            StopWakeRecognizer();
        }

        void Update()
        {
            if (_cooldown > 0f) _cooldown -= Time.deltaTime;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (!Enabled) return;
            if (_bm == null || _bm.Phase != BattlePhase.Fighting) return;

            // Tabキーでも起動できる（音声呼び出しの保険）
            if (_state == AngelState.Idle && _cooldown <= 0f && kb.tabKey.wasPressedThisFrame)
                TryStartAngelSequence();
        }

        void TryStartAngelSequence()
        {
            if (!Enabled) return;
            if (_bm == null || _bm.Phase != BattlePhase.Fighting) return;
            if (_state != AngelState.Idle || _cooldown > 0f) return;
            StartCoroutine(AngelSequence());
        }

        IEnumerator AngelSequence()
        {
            StopWakeRecognizer();
            // 録音フェーズ
            _state = AngelState.Recording;
            ShowStatus("呼びました？ 3秒だけ願いを聞きます！");

            string transcribed  = null;
            bool   recordDone   = false;

            WhisperClient.RecordAndTranscribe(this, recordSeconds,
                text => { transcribed = text; recordDone = true; },
                err  => { Debug.LogWarning("[Angel] Whisper: " + err); recordDone = true; });

            while (!recordDone) yield return null;

            // ギミック決定フェーズ
            _state = AngelState.Processing;
            ShowStatus("天使が考え中...");

            var battleState = BuildBattleState();
            GimmickData gimmick = null;
            bool        decided = false;

            AIAngelClient.DecideGimmick(this, transcribed ?? "", battleState,
                data => { gimmick = data; decided = true; },
                err  => { Debug.LogWarning("[Angel] Angel: " + err); decided = true; });

            float timeout = 18f;
            while (!decided && timeout > 0f) { timeout -= Time.deltaTime; yield return null; }

            if (gimmick == null)
                gimmick = new GimmickData { gimmick = "hp_recover", target = "weaker", value = 0.15f, message = "ちょっと退屈だから…HP少しあげる♪" };

            // 表示 + ギミック適用フェーズ
            _state = AngelState.Displaying;
            ShowAngelMessage(gimmick.message);

            _applier.Apply(gimmick, _bm?.fighter1, _bm?.fighter2);

            // TTSでセリフ読み上げ（失敗しても続行）
            AITTSClient.Speak(this, gimmick.message, _audioSource,
                onError: err => Debug.LogWarning("[AngelTTS] " + err));

            yield return new WaitForSeconds(4f);
            HidePanel();

            _cooldown = cooldownTime;
            _state    = AngelState.Idle;
            StartWakeRecognizer();
        }

        void OnDestroy()
        {
            StopWakeRecognizer();
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
            panelRect.anchoredPosition = new Vector2(-16f, 70f); // 実況テロップの上
            panelRect.sizeDelta        = new Vector2(320f, 80f);

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.55f, 0.18f, 0.72f, 0.85f); // 紫

            _group       = panelGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            // 天使タイトルラベル
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

            // ステータス/メッセージラベル
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
        }

        void StartWakeRecognizer()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_keywordRecognizer != null || wakeWords == null || wakeWords.Length == 0) return;
            _keywordRecognizer = new KeywordRecognizer(wakeWords);
            _keywordRecognizer.OnPhraseRecognized += OnWakeWordRecognized;
            _keywordRecognizer.Start();
#endif
        }

        void StopWakeRecognizer()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_keywordRecognizer == null) return;
            if (_keywordRecognizer.IsRunning) _keywordRecognizer.Stop();
            _keywordRecognizer.OnPhraseRecognized -= OnWakeWordRecognized;
            _keywordRecognizer.Dispose();
            _keywordRecognizer = null;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        void OnWakeWordRecognized(PhraseRecognizedEventArgs args)
        {
            TryStartAngelSequence();
        }
#endif
    }
}
