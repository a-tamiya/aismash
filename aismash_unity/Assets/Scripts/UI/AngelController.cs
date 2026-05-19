using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PromptFighters.AI;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    // 気まぐれ天使コントローラー。
    // 自動タイマーで降臨し、退屈メッセージ表示 + 10秒の音声入力を受け付けてギミックを決定する。
    // BattleManager の Awake で AddComponent される。
    public class AngelController : MonoBehaviour
    {
        public static bool Enabled = true;
        public float recordSeconds = 10f;

        static readonly string[] BoredMessages =
        {
            "なんか退屈だなぁ…何か頼んでもいいよ？",
            "ヒマすぎる…ちょっと手伝ってあげようか？",
            "つまんないから、願い事を叶えてあげる！",
            "ねぇねぇ、何かしてほしいことある？",
            "気まぐれが発動！何か言ってみて♪",
        };

        bool _busy;
        BattleManager       _bm;
        AngelGimmickApplier _applier;
        Coroutine           _loopRoutine;

        CanvasGroup       _group;
        TextMeshProUGUI   _titleLabel;
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
            yield return new WaitForSeconds(10f); // 試合開始10秒後に最初の降臨

            while (true)
            {
                if (Enabled && _bm != null && _bm.Phase == BattlePhase.Fighting && !_busy)
                    yield return StartCoroutine(AngelSequence());

                yield return new WaitForSeconds(15f); // ギミック終了から15秒後に次の降臨
            }
        }

        IEnumerator AngelSequence()
        {
            _busy = true;

            // 1. 退屈メッセージを表示して TTS 再生
            string boredMsg = BoredMessages[Random.Range(0, BoredMessages.Length)];
            ShowDescend(boredMsg);

            bool ttsDone = false;
            AITTSClient.Speak(this, boredMsg, _audioSource,
                onComplete: () => ttsDone = true,
                onError: err => ttsDone = true,
                voice: AITTSClient.AngelVoice,
                volume: 1.6f);

            // TTS が終わるか最大 5 秒待ってから録音開始
            float waited = 0f;
            while (!ttsDone && waited < 5f) { waited += Time.deltaTime; yield return null; }

            // 2. 音声入力受付（recordSeconds 秒）
            ShowListening();
            string transcribed = null;
            bool   recordDone  = false;
            WhisperClient.RecordAndTranscribe(this, recordSeconds,
                text => { transcribed = text; recordDone = true; },
                err  => { Debug.LogWarning("[Angel] Whisper: " + err); recordDone = true; });
            while (!recordDone) yield return null;

            // 3. GPT でギミック決定
            ShowStatus("天使が考え中...");
            var battleState = BuildBattleState();
            GimmickData gimmick = null;
            bool        decided = false;
            AIAngelClient.DecideGimmick(this, transcribed ?? "", battleState,
                data => { gimmick = data; decided = true; },
                err  => { Debug.LogWarning("[Angel] " + err); decided = true; });
            float timeout = 18f;
            while (!decided && timeout > 0f) { timeout -= Time.deltaTime; yield return null; }

            if (gimmick == null)
                gimmick = new GimmickData { gimmick = "hp_recover", target = "weaker", value = 0.15f, message = "ちょっと退屈だから…HP少しあげる♪" };

            // 4. ギミック適用＋メッセージ表示＋TTS
            ShowGimmickMessage(gimmick.message);
            _applier.Apply(gimmick, _bm?.fighter1, _bm?.fighter2);
            AITTSClient.Speak(this, gimmick.message, _audioSource,
                onError: err => Debug.LogWarning("[AngelTTS] " + err),
                voice: AITTSClient.AngelVoice,
                volume: 1.6f);

            yield return new WaitForSeconds(4f);
            HidePanel();

            _busy = false;
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

        void ShowDescend(string msg)
        {
            _titleLabel.text  = "[ 天使降臨中 ]";
            _statusLabel.text = msg;
            _group.alpha      = 1f;
        }

        void ShowListening()
        {
            _titleLabel.text  = "[ 天使降臨中 ]";
            _statusLabel.text = $"お願い事を話してね！ ({recordSeconds:0}秒)";
            _group.alpha      = 1f;
        }

        void ShowStatus(string text)
        {
            _titleLabel.text  = "[ 天使降臨中 ]";
            _statusLabel.text = text;
            _group.alpha      = 1f;
        }

        void ShowGimmickMessage(string msg)
        {
            _titleLabel.text  = "[ 気まぐれ天使 ]";
            _statusLabel.text = msg;
            _group.alpha      = 1f;
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

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(canvasGo.transform, false);

            var panelRect = panelGo.AddComponent<RectTransform>();
            panelRect.anchorMin        = new Vector2(1f, 0f);
            panelRect.anchorMax        = new Vector2(1f, 0f);
            panelRect.pivot            = new Vector2(1f, 0f);
            panelRect.anchoredPosition = new Vector2(-16f, 70f);
            panelRect.sizeDelta        = new Vector2(320f, 80f);

            panelGo.AddComponent<Image>().color = new Color(0.55f, 0.18f, 0.72f, 0.85f);

            _group       = panelGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(panelGo.transform, false);
            var titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.55f);
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(8f, 0f);
            titleRect.offsetMax = new Vector2(-8f, -4f);

            _titleLabel = titleGo.AddComponent<TextMeshProUGUI>();
            UITheme.Apply(_titleLabel, 14f, FontStyles.Bold);
            _titleLabel.color     = new Color(1f, 0.9f, 0.5f);
            _titleLabel.alignment = TextAlignmentOptions.Center;

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

            _audioSource             = canvasGo.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.volume      = 1f;
        }
    }
}
