using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;

namespace PromptFighters.GameFlow
{
    // 初めての人向けの操作チュートリアル。読ませるのではなく「やって覚える」方式。
    // 1つの目標を大きく表示 → プレイヤーの操作を検知 → クリア音＋✓で次へ進む。
    // トレーニング基盤を流用し、既知のプリセットを使う（技構成が標準的なため）。
    public partial class PreBattlePanel : MonoBehaviour
    {
        bool _tutorialActive;
        int  _tutorialStep;
        Fighter _tutFighter;
        FighterAI.CpuLevel _savedCpuLevel;

        // 検知用の状態
        float _tutPrevX, _tutMoveDist, _tutGuardTime;
        bool  _tutJumped, _tutSmashed, _tutGrabbed;
        int   _tutAttackCount;
        bool  _tutStepClearing;

        GameObject      _tutorialPanel;
        TextMeshProUGUI _tutTitle;
        TextMeshProUGUI _tutHint;
        TextMeshProUGUI _tutProgress;
        TextMeshProUGUI _tutCheck;
        CanvasGroup     _tutCheckGroup;

        struct TutorialStep { public string title; public string hint; }

        static readonly TutorialStep[] TutorialSteps =
        {
            new TutorialStep { title = "① 動いてみよう",       hint = "1P: A / D　　パッド: 左スティック・十字キー" },
            new TutorialStep { title = "② ジャンプ！",          hint = "1P: W　　パッド: Y / △　（空中でもう一度で2段ジャンプ）" },
            new TutorialStep { title = "③ ガードで防ぐ",        hint = "1P: 左Shift を長押し　　パッド: RB / RT" },
            new TutorialStep { title = "④ 技を出そう（3回）",   hint = "1P: J / K / L　　パッド: B / A / X" },
            new TutorialStep { title = "⑤ スマッシュ！",        hint = "1P: A か D を素早く2回 → J　　パッド: 右スティックを横に倒す" },
            new TutorialStep { title = "⑥ つかみ",              hint = "1P: G　　パッド: LB / LT　（ガードを無視してつかめる）" },
        };

        void StartTutorial()
        {
            var bm = BattleManager.Instance;
            if (bm == null || _presets == null || _presets.Count == 0) return;
            if (_tutorialActive) return;

            // チュートリアル中は相手を動かさない（CPUを一時オフ）
            _savedCpuLevel = FighterAI.Level;
            FighterAI.Level = FighterAI.CpuLevel.Off;
            BattleManager.RequestedMode = BattleMode.Versus;

            var d1 = PromptCharacterFactory.Clone(_presets[0]);
            var d2 = PromptCharacterFactory.Clone(_presets.Count > 1 ? _presets[1] : _presets[0]);
            EnsureSpriteSet(d1);
            EnsureSpriteSet(d2);

            _tutorialActive = true;
            _tutorialStep = 0;
            _tutStepClearing = false;
            if (_titlePanel != null) _titlePanel.SetActive(false);
            if (_panel != null) _panel.SetActive(false);
            HideGamepadCursors();

            bm.StartTraining(d1, d2);
            _tutFighter = bm.fighter1;
            SubscribeTutorial();

            if (_tutorialPanel != null) _tutorialPanel.SetActive(true);
            ShowTutorialStep(0);
        }

        void SubscribeTutorial()
        {
            if (_tutFighter == null) return;
            _tutFighter.OnJumped      += TutOnJumped;
            _tutFighter.OnGrabAttempt += TutOnGrab;
            var ex = _tutFighter.GetComponent<SkillExecutor>();
            if (ex != null) ex.OnSkillExecuted += TutOnSkill;
        }

        void UnsubscribeTutorial()
        {
            if (_tutFighter == null) return;
            _tutFighter.OnJumped      -= TutOnJumped;
            _tutFighter.OnGrabAttempt -= TutOnGrab;
            var ex = _tutFighter.GetComponent<SkillExecutor>();
            if (ex != null) ex.OnSkillExecuted -= TutOnSkill;
        }

        void TutOnJumped() { if (_tutorialStep == 1) _tutJumped = true; }
        void TutOnGrab()   { if (_tutorialStep == 5) _tutGrabbed = true; }
        void TutOnSkill(SkillSlot s)
        {
            if (_tutorialStep == 3 && s != SkillSlot.SmashSide) _tutAttackCount++;
            if (_tutorialStep == 4 && s == SkillSlot.SmashSide) _tutSmashed = true;
        }

        void UpdateTutorial()
        {
            // Escでいつでも中断してタイトルへ
            if (WasKeyboardCancelPressed()) { EndTutorial(toTitle: true); return; }
            if (_tutStepClearing || _tutFighter == null) return;

            bool done = false;
            switch (_tutorialStep)
            {
                case 0: // 移動
                    _tutMoveDist += Mathf.Abs(_tutFighter.transform.position.x - _tutPrevX);
                    _tutPrevX = _tutFighter.transform.position.x;
                    done = _tutMoveDist > 3f;
                    break;
                case 1: done = _tutJumped; break;
                case 2: // ガード
                    if (_tutFighter.State == FighterState.Guarding) _tutGuardTime += Time.deltaTime;
                    done = _tutGuardTime > 0.4f;
                    break;
                case 3: done = _tutAttackCount >= 3; break;
                case 4: done = _tutSmashed; break;
                case 5: done = _tutGrabbed; break;
            }
            if (done) StartCoroutine(CompleteStepRoutine());
        }

        IEnumerator CompleteStepRoutine()
        {
            _tutStepClearing = true;
            PromptFighters.Audio.GameAudioManager.Instance?.PlayGimmickHeal();

            // 大きな✓を一瞬表示
            if (_tutCheck != null && _tutCheckGroup != null)
            {
                _tutCheck.text = "✓ クリア！";
                float t = 0f;
                while (t < 0.7f)
                {
                    t += Time.unscaledDeltaTime;
                    _tutCheckGroup.alpha = t < 0.15f ? t / 0.15f : Mathf.Clamp01((0.7f - t) / 0.35f);
                    float s = Mathf.Lerp(0.7f, 1.1f, Mathf.Clamp01(t / 0.2f));
                    _tutCheck.rectTransform.localScale = Vector3.one * s;
                    yield return null;
                }
                _tutCheckGroup.alpha = 0f;
            }

            _tutorialStep++;
            ResetStepState();

            if (_tutorialStep >= TutorialSteps.Length)
            {
                yield return FinishTutorialRoutine();
                yield break;
            }
            ShowTutorialStep(_tutorialStep);
            _tutStepClearing = false;
        }

        IEnumerator FinishTutorialRoutine()
        {
            if (_tutTitle != null) _tutTitle.text = "クリア！ 準備OK！";
            if (_tutHint != null)  _tutHint.text  = "さっそく自分だけのキャラを作って戦おう！";
            if (_tutProgress != null) _tutProgress.text = "● ● ● ● ● ●";
            PromptFighters.Audio.GameAudioManager.Instance?.PlayGimmickBuff();
            yield return new WaitForSecondsRealtime(2.4f);
            EndTutorial(toTitle: false);
        }

        void ResetStepState()
        {
            _tutMoveDist = 0f;
            _tutGuardTime = 0f;
            _tutJumped = _tutSmashed = _tutGrabbed = false;
            _tutAttackCount = 0;
            if (_tutFighter != null) _tutPrevX = _tutFighter.transform.position.x;
        }

        void ShowTutorialStep(int step)
        {
            ResetStepState();
            if (step < 0 || step >= TutorialSteps.Length) return;
            if (_tutTitle != null) _tutTitle.text = TutorialSteps[step].title;
            if (_tutHint != null)  _tutHint.text  = TutorialSteps[step].hint;
            if (_tutProgress != null)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < TutorialSteps.Length; i++)
                    sb.Append(i <= step ? "● " : "○ ");
                _tutProgress.text = sb.ToString().TrimEnd();
            }
        }

        void EndTutorial(bool toTitle)
        {
            UnsubscribeTutorial();
            FighterAI.Level = _savedCpuLevel;
            _tutorialActive = false;
            _tutStepClearing = false;
            _tutFighter = null;
            if (_tutorialPanel != null) _tutorialPanel.SetActive(false);
            BattleManager.Instance?.ReturnToSetup(); // 試合を止めてSetupへ（→ ShowPanel でキャラ選択表示）
            if (toTitle) ShowTitlePanel();
            _waitForMenuInputRelease = true; // 直後のEsc/決定が次画面へ貫通しないように
        }

        void BuildTutorialPanel()
        {
            _tutorialPanel = CreateUIObject("TutorialOverlay", transform);
            StretchFull(_tutorialPanel.GetComponent<RectTransform>());
            var cg = _tutorialPanel.AddComponent<CanvasGroup>();
            cg.interactable = false;     // ゲームプレイを妨げない（入力はFighterInputが直接読む）
            cg.blocksRaycasts = false;
            var t = _tutorialPanel.transform;

            // 上部の指示バナー（HPバーの下）。中央のキャラは隠さない。
            var banner = MakePanel(t, "TutBanner", new Vector2(0f, 300f), new Vector2(1200f, 150f),
                new Color(0.02f, 0.025f, 0.05f, 0.82f));
            banner.raycastTarget = false;
            MakeSlantBar(t, "TutBannerTop", new Vector2(0f, 375f), new Vector2(1200f, 5f),
                PromptFighters.UI.UITheme.Gold, 26f);

            _tutTitle = MakeLabel(t, "TutTitle", "",
                new Vector2(0f, 332f), new Vector2(1140f, 60f), 40f, PromptFighters.UI.UITheme.Gold);
            _tutTitle.fontStyle = FontStyles.Bold | FontStyles.Italic;

            _tutHint = MakeLabel(t, "TutHint", "",
                new Vector2(0f, 282f), new Vector2(1140f, 44f), 24f, Color.white);
            _tutHint.fontStyle = FontStyles.Bold;

            _tutProgress = MakeLabel(t, "TutProgress", "",
                new Vector2(0f, 240f), new Vector2(600f, 34f), 24f, PromptFighters.UI.UITheme.InkDim);
            _tutProgress.fontStyle = FontStyles.Bold;

            // クリア時の大きな✓
            var checkGo = CreateUIObject("TutCheck", t);
            var crt = checkGo.GetComponent<RectTransform>();
            crt.anchoredPosition = new Vector2(0f, 40f);
            crt.sizeDelta = new Vector2(900f, 160f);
            _tutCheckGroup = checkGo.AddComponent<CanvasGroup>();
            _tutCheckGroup.alpha = 0f;
            _tutCheck = checkGo.AddComponent<TextMeshProUGUI>();
            PromptFighters.UI.UITheme.Apply(_tutCheck, 92f, FontStyles.Bold | FontStyles.Italic);
            _tutCheck.text = "✓";
            _tutCheck.color = new Color(0.3f, 0.95f, 0.5f);
            _tutCheck.alignment = TextAlignmentOptions.Center;
            _tutCheck.raycastTarget = false;

            MakeLabel(t, "TutQuit", "Escキー: やめる",
                new Vector2(0f, -470f), new Vector2(400f, 30f), 16f, PromptFighters.UI.UITheme.InkDim)
                .fontStyle = FontStyles.Bold;

            _tutorialPanel.SetActive(false);
        }
    }
}
