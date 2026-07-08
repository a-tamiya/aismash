using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;
using PromptFighters.Utils;

namespace PromptFighters.GameFlow
{
    // 初めての人向けの操作チュートリアル。読ませるのではなく「やって覚える」方式。
    // 1人でも2人でも遊べるよう、1P/2Pは独立トラックで並行進行する。
    //  ・各プレイヤーが自分のペースで ①移動→⑦ボイスボール を1つずつクリアする
    //  ・「参加した（何か操作した）プレイヤー全員」がクリアしたら終了
    //    → 1人（1P）だけ操作すれば1Pのクリアで終了、2人操作なら両方のクリアで終了
    //  ・Escでいつでもタイトルへ戻る
    public partial class PreBattlePanel : MonoBehaviour
    {
        bool _tutorialActive;
        FighterAI.CpuLevel _savedCpuLevel;

        // プレイヤーごとの状態（[0]=1P, [1]=2P）
        readonly Fighter[] _tutFighters   = new Fighter[2];
        readonly int[]     _tutStep       = new int[2];
        readonly bool[]    _tutStepClearing= new bool[2];
        readonly bool[]    _tutEngaged     = new bool[2];
        readonly float[]   _tutPrevX       = new float[2];
        readonly float[]   _tutMoveDist    = new float[2];
        readonly float[]   _tutGuardTime   = new float[2];
        readonly bool[]    _tutJumped      = new bool[2];
        readonly bool[]    _tutSmashed     = new bool[2];
        readonly bool[]    _tutGrabbed     = new bool[2];
        readonly bool[]    _tutVoiceBroken = new bool[2];
        // 技ステップ: A/B/Cをそれぞれ1回ずつ出したか（同じ技の連打ではクリアにならない）
        readonly bool[]    _tutSkillADone  = new bool[2];
        readonly bool[]    _tutSkillBDone  = new bool[2];
        readonly bool[]    _tutSkillCDone  = new bool[2];
        // つかみステップ: 一度でも本当に掴んだ（IsHoldingOpponent）ことがあるか。
        // 掴んだ後に離れた（投げた/自動解除）ら、そのステップをクリア扱いにする。
        readonly bool[]    _tutHeldOnce    = new bool[2];

        // 練習用に生成するオブジェクト（練習台ファイター／ボイスボール）。終了時に一括破棄する。
        readonly System.Collections.Generic.List<GameObject> _tutSpawned = new System.Collections.Generic.List<GameObject>();
        readonly VoiceItem[] _tutVoice = new VoiceItem[2];
        // チュートリアル用の練習台。「HP無限・動かないプレイヤー」として本物のFighterをそのまま使う
        // （AI/入力を無効化し、HPを実質無限にするだけ。ノックバック・つかみ・投げは実際の戦闘システムに任せる）。
        readonly Fighter[] _tutDummy = new Fighter[2];
        const int VoiceStepIndex = 6;

        static Sprite _dummySprite;
        static bool   _dummySpriteTried;

        // 各ステップで押すべきボタンを示すコントローラー全体図（該当ボタンが光った状態の画像）。
        // Resources/UI/pad_*.png が無ければ何も表示しない（フォールバック＝非表示）。
        readonly Image[] _tutPadIcon = new Image[2];
        static readonly string[] StepPadIconNames =
        {
            "pad_move",   // ① 移動：左スティック
            "pad_jump",   // ② ジャンプ：Y
            "pad_guard",  // ③ ガード：RB
            "pad_skills", // ④ 技：B/A/X
            "pad_smash",  // ⑤ スマッシュ：右スティック
            "pad_grab",   // ⑥ つかみ：LB
            null,         // ⑦ ボイスボール：専用ボタンなし
        };
        static readonly System.Collections.Generic.Dictionary<string, Sprite> _padSpriteCache =
            new System.Collections.Generic.Dictionary<string, Sprite>();
        static readonly System.Collections.Generic.HashSet<string> _padSpriteTried =
            new System.Collections.Generic.HashSet<string>();

        // Enter Play Mode Options でドメインリロードを無効化しているため、静的キャッシュは
        // エディタの再生を止めても残り続ける。新しいプレイセッション開始のたびに必ずリセットする。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetTutorialDummySpriteCache()
        {
            _dummySprite = null;
            _dummySpriteTried = false;
            _padSpriteCache.Clear();
            _padSpriteTried.Clear();
        }

        // コントローラーアイコン（Resources/UI/pad_*.png）を1回だけ読み込んでキャッシュする。
        // グリーンバック画像として透過処理する。見つからなければnull（呼び出し側で非表示にする）。
        static Sprite GetPadSprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_padSpriteTried.Contains(name))
                return _padSpriteCache.TryGetValue(name, out var cached) ? cached : null;
            _padSpriteTried.Add(name);

            var tex = Resources.Load<Texture2D>("UI/" + name);
            if (tex == null) return null;

            Sprite sprite;
            if (!tex.isReadable)
            {
                Debug.LogWarning($"[Tutorial] UI/{name} は isReadable=false のため透過処理できません（Import設定を確認してください）");
                sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            else
            {
                var processed = WhiteBackgroundRemover.ApplyChromaGreen(tex);
                sprite = Sprite.Create(processed, new Rect(0, 0, processed.width, processed.height),
                    new Vector2(0.5f, 0.5f), 100f);
            }
            _padSpriteCache[name] = sprite;
            return sprite;
        }

        // このプレイヤーの現在ステップに応じてコントローラーアイコンを差し替える（無ければ非表示）。
        void UpdateTutPadIcon(int i, int step)
        {
            if (_tutPadIcon[i] == null) return;
            string name = (step >= 0 && step < StepPadIconNames.Length) ? StepPadIconNames[step] : null;
            var sprite = GetPadSprite(name);
            _tutPadIcon[i].sprite  = sprite;
            _tutPadIcon[i].enabled = sprite != null;
        }

        // 練習台の見た目（グリーンバックのサンドバッグ画像）を1回だけ読み込んでキャッシュする。
        // 画像下部に透明な余白が残っていても地面から浮かないよう、実ピクセルの下端をピボットにする。
        static Sprite GetDummySprite()
        {
            if (_dummySpriteTried) return _dummySprite;
            _dummySpriteTried = true;
            var tex = Resources.Load<Texture2D>("Effects/sandbag");
            if (tex == null) return null;
            if (!tex.isReadable)
            {
                Debug.LogWarning("[Tutorial] Effects/sandbag は isReadable=false のため透過処理できません");
                _dummySprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0f), tex.height * 0.5f);
                return _dummySprite;
            }
            var processed = WhiteBackgroundRemover.ApplyChromaGreen(tex);
            float footPivotY = EstimateDummyFootPivotY(processed);
            _dummySprite = Sprite.Create(processed, new Rect(0, 0, processed.width, processed.height),
                new Vector2(0.5f, footPivotY), processed.height * 0.5f);
            return _dummySprite;
        }

        static float EstimateDummyFootPivotY(Texture2D processed)
        {
            const int AlphaThreshold = 20;
            int w = processed.width, h = processed.height;
            var px = processed.GetPixels32();
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                    if (px[row + x].a >= AlphaThreshold) return (float)y / h;
            }
            return 0f;
        }

        // イベント購読の解除用に保持するデリゲート
        readonly System.Action[]            _hJump  = new System.Action[2];
        readonly System.Action[]            _hGrab  = new System.Action[2];
        readonly System.Action<SkillSlot>[] _hSkill = new System.Action<SkillSlot>[2];

        // UI（左=1P / 右=2P の2カラム）
        GameObject          _tutorialPanel;
        readonly CanvasGroup[]     _tutBannerGroup = new CanvasGroup[2]; // カラム全体（ボイスボール取得中に隠す用）
        readonly TextMeshProUGUI[] _tutTitle    = new TextMeshProUGUI[2];
        readonly TextMeshProUGUI[] _tutHint     = new TextMeshProUGUI[2];
        readonly TextMeshProUGUI[] _tutProgress = new TextMeshProUGUI[2];
        readonly TextMeshProUGUI[] _tutCheck    = new TextMeshProUGUI[2];
        readonly CanvasGroup[]     _tutCheckGrp = new CanvasGroup[2];
        TextMeshProUGUI     _tutFinish;
        CanvasGroup         _tutFinishGrp;

        struct TutorialStep { public string title, hint; }

        // ヒントはゲームパッド前提（キーボード表記は出さない）
        static readonly TutorialStep[] TutorialSteps =
        {
            new TutorialStep { title = "① 動いてみよう",     hint = "左スティック / 十字キーで動く" },
            new TutorialStep { title = "② ジャンプ！",        hint = "Y / △ でジャンプ（空中でもう一度で2段ジャンプ）" },
            new TutorialStep { title = "③ ガードで防ぐ",      hint = "RB / RT を押しっぱなしでガード" },
            new TutorialStep { title = "④ 技を出そう（3種類）", hint = "B / A / X をそれぞれ1回ずつ出す" },
            new TutorialStep { title = "⑤ スマッシュ！",      hint = "右スティックを横に倒してスマッシュ" },
            new TutorialStep { title = "⑥ つかんで投げよう",  hint = "サンドバッグに近づいて LB / LT でつかむ" },
            new TutorialStep { title = "⑦ ボイスボール！",   hint = "光る球を攻撃してこわし、マイクに願いを話そう！" },
        };

        // プレイヤー自身が操作するチュートリアル用アバターの名前（1P用/2P用）。
        // ゲーム内で生成・保存済みならそれを使い、未生成ならプリセット先頭2体にフォールバックする
        // （StartTutorial参照。練習台＝サンドバッグ側は常に固定でこれとは別）。
        static readonly string[] TutorialDummyCharacterNames = { "ケンゴ", "サヤ" };

        void StartTutorial()
        {
            var bm = BattleManager.Instance;
            if (bm == null || _presets == null || _presets.Count == 0) return;
            if (_tutorialActive) return;

            // チュートリアル中は相手を動かさない（CPUを一時オフ）
            _savedCpuLevel = FighterAI.Level;
            FighterAI.Level = FighterAI.CpuLevel.Off;
            BattleManager.RequestedMode = BattleMode.Versus;

            // プレイヤー自身が操作するチュートリアル用アバターは「ケンゴ」「サヤ」を優先して使う
            // （生成済みなら）。未生成ならこれまで通りプリセット先頭2体にフォールバックする。
            var preset1 = CharacterSaveManager.LoadByName(TutorialDummyCharacterNames[0]) ?? _presets[0];
            var preset2 = CharacterSaveManager.LoadByName(TutorialDummyCharacterNames[1])
                ?? (_presets.Count > 1 ? _presets[1] : _presets[0]);
            var d1 = PromptCharacterFactory.Clone(preset1);
            var d2 = PromptCharacterFactory.Clone(preset2);
            EnsureSpriteSet(d1);
            EnsureSpriteSet(d2);

            _tutorialActive = true;
            for (int i = 0; i < 2; i++)
            {
                _tutStep[i] = 0;
                _tutStepClearing[i] = false;
                _tutEngaged[i] = false;
                ResetStepState(i);
            }
            if (_titlePanel != null) _titlePanel.SetActive(false);
            if (_panel != null) _panel.SetActive(false);
            HideGamepadCursors();

            bm.StartTraining(d1, d2);
            _tutFighters[0] = bm.fighter1;
            _tutFighters[1] = bm.fighter2;
            SubscribeTutorial();

            // 配置が終わったファイターの現在Xを基準に取り直す（スポーン移動を誤検知しないため）
            for (int i = 0; i < 2; i++)
            {
                _tutMoveDist[i] = 0f;
                _tutEngaged[i]  = false;
                _tutVoiceBroken[i] = false;
                if (_tutFighters[i] != null) _tutPrevX[i] = _tutFighters[i].transform.position.x;
            }

            // 各プレイヤーの前に練習台（HP無限・動かないプレイヤー）を用意する（常に2体）
            SpawnTutorialDummies();

            if (_tutFinishGrp != null) _tutFinishGrp.alpha = 0f;
            if (_tutorialPanel != null) _tutorialPanel.SetActive(true);
            for (int i = 0; i < 2; i++)
            {
                if (_tutCheckGrp[i] != null) _tutCheckGrp[i].alpha = 0f;
                if (_tutBannerGroup[i] != null) _tutBannerGroup[i].alpha = 1f;
                ShowStepFor(i);
            }
        }

        void SubscribeTutorial()
        {
            for (int i = 0; i < 2; i++)
            {
                var f = _tutFighters[i];
                if (f == null) continue;
                int idx = i;
                _hJump[idx]  = () => { if (_tutStep[idx] == 1) _tutJumped[idx] = true; TutNudge(idx); };
                // つかみ自体は本物のFighterのグラブ機構がそのまま処理する（近づいてボタンを押せば掴める）。
                // ここでは「参加した」記録だけ行う。
                _hGrab[idx]  = () => TutNudge(idx);
                _hSkill[idx] = (s) =>
                {
                    if (_tutStep[idx] == 3)
                    {
                        switch (s)
                        {
                            case SkillSlot.AttackA: _tutSkillADone[idx] = true; break;
                            case SkillSlot.AttackB: _tutSkillBDone[idx] = true; break;
                            case SkillSlot.AttackC: _tutSkillCDone[idx] = true; break;
                        }
                        UpdateStep3Hint(idx);
                    }
                    if (_tutStep[idx] == 4 && s == SkillSlot.SmashSide) _tutSmashed[idx] = true;
                    TutNudge(idx);
                };
                f.OnJumped      += _hJump[idx];
                f.OnGrabAttempt += _hGrab[idx];
                var ex = f.GetComponent<SkillExecutor>();
                if (ex != null) ex.OnSkillExecuted += _hSkill[idx];
            }
        }

        void UnsubscribeTutorial()
        {
            for (int i = 0; i < 2; i++)
            {
                var f = _tutFighters[i];
                if (f == null) continue;
                if (_hJump[i]  != null) f.OnJumped      -= _hJump[i];
                if (_hGrab[i]  != null) f.OnGrabAttempt -= _hGrab[i];
                var ex = f.GetComponent<SkillExecutor>();
                if (ex != null && _hSkill[i] != null) ex.OnSkillExecuted -= _hSkill[i];
                _hJump[i] = null; _hGrab[i] = null; _hSkill[i] = null;
            }
        }

        // 何か操作があった＝そのプレイヤーは参加中。
        void TutNudge(int i)
        {
            _tutEngaged[i] = true;
        }

        // 技ステップ中、まだ出していない技を「残り: B A」のようにヒントへ反映する。
        void UpdateStep3Hint(int i)
        {
            if (_tutHint[i] == null) return;
            var remaining = new System.Text.StringBuilder();
            if (!_tutSkillADone[i]) remaining.Append("B ");
            if (!_tutSkillBDone[i]) remaining.Append("A ");
            if (!_tutSkillCDone[i]) remaining.Append("X ");
            _tutHint[i].text = remaining.Length > 0
                ? $"残り: {remaining.ToString().Trim()} を出そう"
                : "OK！";
        }

        // 各プレイヤーの少し前（内側）に練習台ファイターを設置する（常に2体、各プレイヤーに1体ずつ）。
        // 「HP無限・動かないプレイヤー」の実体は本物のFighter（bm.bossの複製）。AI/入力を無効化し、
        // HPを実質無限にするだけで、ノックバック・つかみ・投げは通常の戦闘システムがそのまま処理する。
        // 見た目は常にサンドバッグ画像（プレイヤー自身が操作するアバターとは別。そちらはStartTutorial参照）。
        void SpawnTutorialDummies()
        {
            var bm = BattleManager.Instance;
            if (bm == null || bm.boss == null) return;
            float groundY = bm.StageGroundY;
            var fallbackSprite = GetDummySprite();

            for (int i = 0; i < 2; i++)
            {
                var f = _tutFighters[i];
                if (f == null) continue;

                var dummyGo = Instantiate(bm.boss.gameObject);
                dummyGo.name = i == 0 ? "TutorialDummy1P" : "TutorialDummy2P";
                dummyGo.SetActive(true);
                var dummy = dummyGo.GetComponent<Fighter>();
                if (dummy == null) { Destroy(dummyGo); continue; }

                // 動かない：AIにも入力にも操作されないようにする。
                var ai = dummy.GetComponent<FighterAI>();
                if (ai != null) ai.enabled = false;
                var input = dummy.GetComponent<FighterInput>();
                if (input != null) input.enabled = false;

                var data = new CharacterData { characterName = "サンドバッグ" };
                SampleSkillLibrary.EquipDefaults(data);
                if (fallbackSprite != null)
                {
                    data.characterSprite = fallbackSprite;
                    for (int s = 0; s < 15; s++) data.spriteSet.Set((CharacterSpriteId)s, fallbackSprite);
                }

                dummy.ResetGimmickStats();
                dummy.GetComponent<SkillExecutor>()?.LoadCharacter(data);
                dummy.ApplyCharacterStats(data.stats);
                dummy.SetGrabThrowParameters(data.grabParameters, data.throwParameters);
                dummy.SetSizeScale(data.sizeScale > 0f ? data.sizeScale : 1f);
                if (data.characterSprite != null) dummy.SetCharacterSprites(data.spriteSet);
                dummy.maxHP = 999999f; // HP無限扱い（ApplyCharacterStatsのクランプを上書き）
                // フレンドリーファイア回避のため、担当プレイヤーと逆チームにする（Hitboxの陣営判定対策）。
                dummy.Team = f.Team == FighterTeam.Players ? FighterTeam.Enemies : FighterTeam.Players;

                float scale = bm.fighterScale;
                dummy.transform.localScale = new Vector3(scale, scale, dummy.transform.localScale.z);

                float sign = f.FacingRight ? 1f : -1f;
                Vector3 pos = new Vector3(f.transform.position.x + sign * 2.1f, groundY, 0f);
                dummy.ResetForBattle(pos, faceRight: !f.FacingRight); // プレイヤーの方を向かせる
                dummy.GetComponent<SkillExecutor>()?.ResetSkillState();

                f.Opponent = dummy; // 実際のつかみ・攻撃対象をこの練習台にする

                _tutDummy[i] = dummy;
                _tutSpawned.Add(dummyGo);
            }
        }

        // ボイスボールのステップに入ったら、そのプレイヤーの近くにボイスボールを出す。
        // 2人同時に出るとカオスになるため、既にどちらかの分が出ている間は自分の分を保留する（先着順）。
        // UpdateTutorial()から毎フレーム呼ばれ、相手の分が片付き次第このプレイヤーの分を出す。
        void SpawnTutorialVoiceBall(int i)
        {
            var f = _tutFighters[i];
            if (f == null || _tutVoice[i] != null || _tutVoiceBroken[i]) return;
            if (_tutVoice[1 - i] != null)
            {
                if (_tutHint[i] != null) _tutHint[i].text = "相手のボイスボールが終わるまで少し待とう…";
                return;
            }
            float groundY = BattleManager.Instance != null ? BattleManager.Instance.StageGroundY : -2.3f;
            float sign = f.FacingRight ? 1f : -1f;
            Vector2 pos = new Vector2(f.transform.position.x + sign * 2.0f, groundY + 1.0f);
            int idx = i;
            if (_tutHint[i] != null) _tutHint[i].text = TutorialSteps[VoiceStepIndex].hint;
            _tutVoice[i] = VoiceItem.Spawn(pos, 1.2f, breaker =>
            {
                _tutVoice[idx] = null;
                // 実際の取得シーケンス（スロー＋マイク音声入力＋ギミック適用）を体験させる。
                // 取得が終わったらそのプレイヤーのステップをクリアにする。
                var angel = BattleManager.Instance != null
                    ? BattleManager.Instance.GetComponent<PromptFighters.UI.AngelController>() : null;
                if (angel != null)
                {
                    // AngelControllerの取得バナーは画面上部に全幅で表示され、
                    // 1P/2P両方のチュートリアルバナーと同じ帯に重なるため、
                    // 取得演出中は両方のチュートリアルバナーを一時的に隠す。
                    SetTutorialBannerVisible(0, false);
                    SetTutorialBannerVisible(1, false);
                    angel.BeginAcquire(breaker ?? _tutFighters[idx], () =>
                    {
                        _tutVoiceBroken[idx] = true;
                        SetTutorialBannerVisible(0, true);
                        SetTutorialBannerVisible(1, true);
                    });
                }
                else
                {
                    _tutVoiceBroken[idx] = true; // 保険：AngelControllerが無ければ破壊だけで完了
                }
            });
            if (_tutVoice[i] != null) _tutSpawned.Add(_tutVoice[i].gameObject);
        }

        void SetTutorialBannerVisible(int i, bool visible)
        {
            if (_tutBannerGroup[i] != null) _tutBannerGroup[i].alpha = visible ? 1f : 0f;
        }

        void UpdateTutorial()
        {
            if (WasGameplayCancelPressed()) { EndTutorial(toTitle: true); return; }

            for (int i = 0; i < 2; i++)
            {
                var f = _tutFighters[i];
                if (f == null || _tutStepClearing[i]) continue;
                int step = _tutStep[i];
                if (step >= TutorialSteps.Length) continue;

                // 練習台を持っている間/持っていた後は、ステップに関わらず検出する
                // （⑥のステップ以外でも自由に掴んで投げて遊べるようにするため、switch外で判定する）。
                // 実際の掴み・投げは本物のFighterのグラブ機構が処理するので、ここでは
                // 「一度でも持った→離れた（投げた/自動解除）」の遷移だけを見る。
                bool holdingNow = f.IsHoldingOpponent;
                if (holdingNow)
                {
                    if (!_tutHeldOnce[i] && step == 5 && _tutHint[i] != null)
                        _tutHint[i].text = "スティックを倒して投げよう！";
                    _tutHeldOnce[i] = true;
                }
                else if (_tutHeldOnce[i])
                {
                    _tutHeldOnce[i] = false;
                    if (step == 5) _tutGrabbed[i] = true;
                }

                bool done = false;
                switch (step)
                {
                    case 0: // 移動（テレポート・スポーン移動は1フレームの移動量が大きいので除外）
                        float dx = Mathf.Abs(f.transform.position.x - _tutPrevX[i]);
                        if (dx > 0.0005f && dx < 1f) { _tutMoveDist[i] += dx; if (_tutMoveDist[i] > 0.4f) TutNudge(i); }
                        _tutPrevX[i] = f.transform.position.x;
                        done = _tutMoveDist[i] > 3f;
                        break;
                    case 1: done = _tutJumped[i]; break;
                    case 2: // ガード
                        if (f.State == FighterState.Guarding) { _tutGuardTime[i] += Time.deltaTime; TutNudge(i); }
                        done = _tutGuardTime[i] > 0.4f;
                        break;
                    case 3: done = _tutSkillADone[i] && _tutSkillBDone[i] && _tutSkillCDone[i]; break;
                    case 4: done = _tutSmashed[i]; break;
                    case 5: done = _tutGrabbed[i]; break; // つかんで投げる（判定は上のswitch外で共通処理）
                    case VoiceStepIndex:
                        // 相手の分が出ている間は保留されるので、空き次第このプレイヤーの分を出す。
                        SpawnTutorialVoiceBall(i);
                        done = _tutVoiceBroken[i];
                        break;
                }
                if (done) StartCoroutine(CompleteStepRoutine(i));
            }
        }

        IEnumerator CompleteStepRoutine(int i)
        {
            _tutStepClearing[i] = true;
            PromptFighters.Audio.GameAudioManager.Instance?.PlayGimmickHeal();

            // そのプレイヤーのカラムに✓を一瞬表示
            if (_tutCheck[i] != null && _tutCheckGrp[i] != null)
            {
                _tutCheck[i].text = "✓";
                float t = 0f;
                while (t < 0.55f)
                {
                    t += Time.unscaledDeltaTime;
                    _tutCheckGrp[i].alpha = t < 0.12f ? t / 0.12f : Mathf.Clamp01((0.55f - t) / 0.3f);
                    _tutCheck[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.15f, Mathf.Clamp01(t / 0.18f));
                    yield return null;
                }
                _tutCheckGrp[i].alpha = 0f;
            }

            _tutStep[i]++;
            ResetStepState(i);

            if (_tutStep[i] >= TutorialSteps.Length)
            {
                ShowColumnClear(i);
                if (IsTutorialComplete())
                {
                    yield return FinishTutorialRoutine();
                    yield break;
                }
            }
            else ShowStepFor(i);

            _tutStepClearing[i] = false;
        }

        // 参加中のプレイヤーが全員クリアしたか。誰も参加していなければ未完了。
        bool IsTutorialComplete()
        {
            bool anyEngaged = _tutEngaged[0] || _tutEngaged[1];
            if (!anyEngaged) return false;
            for (int i = 0; i < 2; i++)
                if (_tutEngaged[i] && _tutStep[i] < TutorialSteps.Length) return false;
            return true;
        }

        IEnumerator FinishTutorialRoutine()
        {
            if (_tutFinish != null) _tutFinish.text = "クリア！ 準備OK！\nさっそく自分だけのキャラを作って戦おう！";
            PromptFighters.Audio.GameAudioManager.Instance?.PlayGimmickBuff();
            if (_tutFinishGrp != null)
            {
                float t = 0f;
                while (t < 0.3f) { t += Time.unscaledDeltaTime; _tutFinishGrp.alpha = t / 0.3f; yield return null; }
                _tutFinishGrp.alpha = 1f;
            }
            yield return new WaitForSecondsRealtime(2.4f);
            EndTutorial(toTitle: false);
        }

        void ResetStepState(int i)
        {
            _tutMoveDist[i] = 0f;
            _tutGuardTime[i] = 0f;
            _tutJumped[i] = _tutSmashed[i] = _tutGrabbed[i] = false;
            _tutSkillADone[i] = _tutSkillBDone[i] = _tutSkillCDone[i] = false;
            _tutHeldOnce[i] = false;
            if (_tutFighters[i] != null) _tutPrevX[i] = _tutFighters[i].transform.position.x;
        }

        void ShowStepFor(int i)
        {
            int step = _tutStep[i];
            if (step < 0 || step >= TutorialSteps.Length) return;
            var s = TutorialSteps[step];
            if (_tutTitle[i] != null) _tutTitle[i].text = s.title;
            if (_tutHint[i] != null)  _tutHint[i].text  = s.hint;
            // ボイスボールのステップに入ったら球を出す
            if (step == VoiceStepIndex) SpawnTutorialVoiceBall(i);
            if (_tutProgress[i] != null)
            {
                var sb = new System.Text.StringBuilder();
                for (int k = 0; k < TutorialSteps.Length; k++) sb.Append(k <= step ? "● " : "○ ");
                _tutProgress[i].text = sb.ToString().TrimEnd();
            }
            UpdateTutPadIcon(i, step);
        }

        void ShowColumnClear(int i)
        {
            if (_tutTitle[i] != null) _tutTitle[i].text = "クリア！ ✓";
            if (_tutPadIcon[i] != null) _tutPadIcon[i].enabled = false;
            if (_tutHint[i] != null)
            {
                // 相手がまだ挑戦中なら「待っています」を出す
                bool otherBusy = _tutEngaged[1 - i] && _tutStep[1 - i] < TutorialSteps.Length;
                _tutHint[i].text = otherBusy ? "相手のクリアを待っています…" : "";
            }
            if (_tutProgress[i] != null) _tutProgress[i].text = "● ● ● ● ● ● ●";
        }

        void EndTutorial(bool toTitle)
        {
            UnsubscribeTutorial();
            FighterAI.Level = _savedCpuLevel;
            _tutorialActive = false;
            for (int i = 0; i < 2; i++)
            {
                _tutStepClearing[i] = false;
                _tutFighters[i] = null;
                _tutVoice[i] = null;
                _tutDummy[i] = null;
            }
            // 練習用に出した練習台・ボイスボールを片付ける
            foreach (var go in _tutSpawned) if (go != null) Destroy(go);
            _tutSpawned.Clear();
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

            BuildTutColumn(t, 0, -340f, PromptFighters.UI.UITheme.P1Neon);
            BuildTutColumn(t, 1,  340f, PromptFighters.UI.UITheme.P2Neon);

            // 中央の完了メッセージ（最初は非表示）
            var finGo = CreateUIObject("TutFinish", t);
            var frt = finGo.GetComponent<RectTransform>();
            frt.anchoredPosition = new Vector2(0f, 40f);
            frt.sizeDelta = new Vector2(1200f, 240f);
            _tutFinishGrp = finGo.AddComponent<CanvasGroup>();
            _tutFinishGrp.alpha = 0f;
            var finBg = finGo.AddComponent<Image>();
            finBg.sprite = PromptFighters.UI.UITheme.VGradient; finBg.type = Image.Type.Simple;
            finBg.color = new Color(0.02f, 0.025f, 0.05f, 0.92f);
            finBg.raycastTarget = false;
            _tutFinish = MakeLabel(finGo.transform, "TutFinishText", "",
                new Vector2(0f, 0f), new Vector2(1140f, 220f), 40f, PromptFighters.UI.UITheme.Gold);
            _tutFinish.fontStyle = FontStyles.Bold | FontStyles.Italic;

            MakeLabel(t, "TutQuit", "Startボタン: やめる　　1人でも2人でも遊べます",
                new Vector2(0f, -470f), new Vector2(700f, 30f), 16f, PromptFighters.UI.UITheme.InkDim)
                .fontStyle = FontStyles.Bold;

            _tutorialPanel.SetActive(false);
        }

        void BuildTutColumn(Transform parent, int i, float cx, Color pColor)
        {
            // このカラムの要素をまとめるコンテナ。ボイスボール取得中はAngelControllerの取得バナーと
            // 画面上部の同じ帯に重なるため、CanvasGroupでカラムごと一時的に隠せるようにする。
            var container = CreateUIObject(i == 0 ? "TutColumn1P" : "TutColumn2P", parent);
            var containerRt = container.GetComponent<RectTransform>();
            containerRt.anchoredPosition = new Vector2(cx, 0f);
            _tutBannerGroup[i] = container.AddComponent<CanvasGroup>();
            var t = container.transform;

            var banner = MakePanel(t, "TutBanner", new Vector2(0f, 300f), new Vector2(640f, 150f),
                new Color(0.02f, 0.025f, 0.05f, 0.85f));
            banner.raycastTarget = false;
            MakeSlantBar(t, "TutTop", new Vector2(0f, 375f), new Vector2(640f, 5f), pColor, i == 0 ? 22f : -22f);

            MakeSlantBar(t, "TutBadgePlate", new Vector2(-280f, 345f), new Vector2(84f, 40f), pColor, i == 0 ? 12f : -12f);
            MakeLabel(t, "TutBadge", i == 0 ? "1P" : "2P",
                new Vector2(-280f, 345f), new Vector2(84f, 40f), 24f, Color.white)
                .fontStyle = FontStyles.Bold | FontStyles.Italic;

            _tutTitle[i] = MakeLabel(t, "TutTitle", "",
                new Vector2(24f, 342f), new Vector2(560f, 50f), 30f, pColor);
            _tutTitle[i].fontStyle = FontStyles.Bold | FontStyles.Italic;

            _tutHint[i] = MakeLabel(t, "TutHint", "",
                new Vector2(0f, 296f), new Vector2(600f, 40f), 20f, Color.white);
            _tutHint[i].fontStyle = FontStyles.Bold;

            _tutProgress[i] = MakeLabel(t, "TutProg", "",
                new Vector2(0f, 258f), new Vector2(500f, 32f), 20f, PromptFighters.UI.UITheme.InkDim);
            _tutProgress[i].fontStyle = FontStyles.Bold;

            // 押すべきボタンが光ったコントローラー全体図（バナーの下）。画像未着時は非表示。
            var padGo = CreateUIObject(i == 0 ? "TutPad1P" : "TutPad2P", t);
            var padRt = padGo.GetComponent<RectTransform>();
            padRt.anchoredPosition = new Vector2(0f, 160f);
            padRt.sizeDelta = new Vector2(280f, 170f);
            _tutPadIcon[i] = padGo.AddComponent<Image>();
            _tutPadIcon[i].raycastTarget = false;
            _tutPadIcon[i].preserveAspect = true;
            _tutPadIcon[i].enabled = false;

            // クリア時の✓（そのカラムの中央。バナーとは別グループなので取得演出中も見える）
            var checkGo = CreateUIObject(i == 0 ? "TutCheck1P" : "TutCheck2P", parent);
            var crt = checkGo.GetComponent<RectTransform>();
            crt.anchoredPosition = new Vector2(cx, 300f);
            crt.sizeDelta = new Vector2(300f, 150f);
            _tutCheckGrp[i] = checkGo.AddComponent<CanvasGroup>();
            _tutCheckGrp[i].alpha = 0f;
            _tutCheck[i] = checkGo.AddComponent<TextMeshProUGUI>();
            PromptFighters.UI.UITheme.Apply(_tutCheck[i], 96f, FontStyles.Bold | FontStyles.Italic);
            _tutCheck[i].text = "✓";
            _tutCheck[i].color = new Color(0.3f, 0.95f, 0.5f);
            _tutCheck[i].alignment = TextAlignmentOptions.Center;
            _tutCheck[i].raycastTarget = false;
        }
    }
}
