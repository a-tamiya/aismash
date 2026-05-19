using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PromptFighters.AI;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    // 気まぐれ天使コントローラー。
    // 自動タイマーで降臨 → 退屈メッセージ + 音声入力受付 → ギミック決定 → 中央大表示。
    public class AngelController : MonoBehaviour
    {
        public static bool Enabled = true;
        public float recordSeconds = 5f;

        static readonly string[] BoredMessages =
        {
            "なんか退屈だなぁ…何か頼んでもいいよ？",
            "ヒマすぎる…ちょっと手伝ってあげようか？",
            "つまんないから、願い事を叶えてあげる！",
            "ねぇねぇ、何かしてほしいことある？",
            "気まぐれが発動！何か言ってみて♪",
        };

        bool          _busy;
        BattleManager _bm;
        AngelGimmickApplier _applier;
        Coroutine     _loopRoutine;

        // 上部バナー（天使降臨中）
        CanvasGroup     _bannerGroup;
        TextMeshProUGUI _titleLabel;
        TextMeshProUGUI _statusLabel;

        // 中央大表示（ギミック効果）
        CanvasGroup     _effectGroup;
        TextMeshProUGUI _effectLabel;
        Coroutine       _effectFade;

        // 下部字幕欄（天使の発言）
        CanvasGroup     _subtitleGroup;
        TextMeshProUGUI _subtitleLabel;

        AudioSource _audioSource;

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
            yield return new WaitForSeconds(10f);
            while (true)
            {
                if (Enabled && _bm != null && _bm.Phase == BattlePhase.Fighting && !_busy)
                    yield return StartCoroutine(AngelSequence());
                yield return new WaitForSeconds(15f);
            }
        }

        IEnumerator AngelSequence()
        {
            _busy = true;

            // 1. 退屈メッセージを表示 + TTS
            string boredMsg = BoredMessages[Random.Range(0, BoredMessages.Length)];
            ShowBanner("[ 天使降臨中 ]", boredMsg);
            ShowSubtitle(boredMsg);

            bool ttsDone = false;
            AITTSClient.Speak(this, boredMsg, _audioSource,
                onComplete: () => ttsDone = true,
                onError: err => ttsDone = true,
                voice: AITTSClient.AngelVoice,
                volume: 2.2f);

            float waited = 0f;
            while (!ttsDone && waited < 5f) { waited += Time.deltaTime; yield return null; }

            // 2. 音声入力受付
            ShowBanner("[ 天使降臨中 ]", $"お願い事を話してね！ ({recordSeconds:0}秒)");
            string transcribed = null;
            bool   recordDone  = false;
            WhisperClient.RecordAndTranscribe(this, recordSeconds,
                text => { transcribed = text; recordDone = true; },
                err  => { Debug.LogWarning("[Angel] Whisper: " + err); recordDone = true; });
            while (!recordDone) yield return null;

            // 3. ギミック決定
            ShowBanner("[ 天使降臨中 ]", "天使が考え中...");
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

            // 4. ギミック適用 + バナー更新 + 中央大表示 + TTS
            ShowBanner("[ 気まぐれ天使 ]", gimmick.message);
            ShowSubtitle(gimmick.message);
            _applier.Apply(gimmick, _bm?.fighter1, _bm?.fighter2);
            ShowEffectCenter(BuildEffectText(gimmick));
            AITTSClient.Speak(this, gimmick.message, _audioSource,
                onError: err => Debug.LogWarning("[AngelTTS] " + err),
                voice: AITTSClient.AngelVoice,
                volume: 2.2f);

            yield return new WaitForSeconds(4f);
            HideBanner();
            HideSubtitle();

            _busy = false;
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
            "obstacle_tilt"     => "斜め足場出現 ！",
            _              => g,
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
            yield return new WaitForSeconds(3f);
            float t = 0f;
            while (t < 0.6f) { t += Time.deltaTime; _effectGroup.alpha = 1f - t / 0.6f; yield return null; }
            _effectGroup.alpha = 0f;
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

        void ShowBanner(string title, string status)
        {
            _titleLabel.text  = title;
            _statusLabel.text = status;
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
            var canvasGo = new GameObject("AngelCanvas");
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

            // ── 上部バナー（背景なし・全幅）──
            var bannerGo = new GameObject("AngelBanner");
            bannerGo.transform.SetParent(canvasGo.transform, false);

            var bannerRect = bannerGo.AddComponent<RectTransform>();
            bannerRect.anchorMin        = new Vector2(0f, 1f);
            bannerRect.anchorMax        = new Vector2(1f, 1f);
            bannerRect.pivot            = new Vector2(0.5f, 1f);
            bannerRect.anchoredPosition = new Vector2(0f, -90f);  // HPバー(~74px)の下
            bannerRect.sizeDelta        = new Vector2(0f, 160f);

            _bannerGroup       = bannerGo.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f;

            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(bannerGo.transform, false);
            var titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(20f, 0f);
            titleRect.offsetMax = new Vector2(-20f, -8f);

            _titleLabel = titleGo.AddComponent<TextMeshProUGUI>();
            UITheme.Apply(_titleLabel, 72f, FontStyles.Bold);
            _titleLabel.color            = new Color(1f, 0.95f, 0.35f);
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

            // ── 中央大表示（ギミック効果・背景なし）──
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
            UITheme.Apply(_effectLabel, 80f, FontStyles.Bold);
            _effectLabel.color     = new Color(1f, 0.95f, 0.3f);
            _effectLabel.alignment = TextAlignmentOptions.Center;

            // ── 下部字幕欄（天使の発言）──
            var subGo = new GameObject("AngelSubtitle");
            subGo.transform.SetParent(canvasGo.transform, false);

            var subRect = subGo.AddComponent<RectTransform>();
            subRect.anchorMin        = new Vector2(0f, 0f);
            subRect.anchorMax        = new Vector2(1f, 0f);
            subRect.pivot            = new Vector2(0.5f, 0f);
            subRect.anchoredPosition = new Vector2(0f, 100f);  // 実況字幕(100px)の上
            subRect.sizeDelta        = new Vector2(0f, 90f);

            _subtitleGroup       = subGo.AddComponent<CanvasGroup>();
            _subtitleGroup.alpha = 0f;

            var subBg = subGo.AddComponent<UnityEngine.UI.Image>();
            subBg.color = new Color(0f, 0f, 0f, 0.55f);

            var subTextGo = new GameObject("SubtitleText");
            subTextGo.transform.SetParent(subGo.transform, false);
            var subTextRect = subTextGo.AddComponent<RectTransform>();
            subTextRect.anchorMin = Vector2.zero;
            subTextRect.anchorMax = Vector2.one;
            subTextRect.offsetMin = new Vector2(24f, 8f);
            subTextRect.offsetMax = new Vector2(-24f, -8f);

            _subtitleLabel = subTextGo.AddComponent<TextMeshProUGUI>();
            UITheme.Apply(_subtitleLabel, 40f);
            _subtitleLabel.color            = new Color(1f, 0.95f, 0.35f);
            _subtitleLabel.alignment        = TextAlignmentOptions.Center;
            _subtitleLabel.textWrappingMode = TextWrappingModes.Normal;
            _subtitleLabel.overflowMode     = TextOverflowModes.Truncate;

            _audioSource             = canvasGo.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.volume      = 1f;
        }
    }
}
