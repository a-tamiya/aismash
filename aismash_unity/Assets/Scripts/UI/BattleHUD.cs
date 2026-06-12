using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;

namespace PromptFighters.UI
{
    [RequireComponent(typeof(Canvas))]
    public class BattleHUD : MonoBehaviour
    {
        // ── Palette (アーケード格ゲー調) ───────────────────────────────
        static readonly Color BgDeep    = UITheme.Steel;
        static readonly Color BgMid     = new Color(0.05f, 0.06f, 0.09f, 0.98f);
        static readonly Color BgBar     = UITheme.SteelDark;
        static readonly Color P1Col     = UITheme.P1Neon;
        static readonly Color P2Col     = UITheme.P2Neon;
        static readonly Color TextWht   = UITheme.Ink;
        static readonly Color TextDim   = UITheme.InkDim;
        static readonly Color GuardFull = new Color(0.30f, 0.80f, 1.00f);
        static readonly Color GuardLow  = new Color(0.95f, 0.30f, 0.30f);
        static readonly Color TimerEdge = UITheme.Gold;
        static readonly Color GlowLine  = new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.55f);
        static readonly Color UrgentCol = UITheme.Urgent;
        static readonly Color Bevel     = new Color(UITheme.SteelLight.r, UITheme.SteelLight.g, UITheme.SteelLight.b, 0.55f);

        // バーのスラント量(px)。1P/2Pで対称になるよう符号反転して使う。
        const float Slant = 16f;

        static readonly Gradient HpGrad = MakeHpGrad();
        static Gradient MakeHpGrad()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1.00f, 0.16f, 0.14f), 0.00f),
                    new GradientColorKey(new Color(1.00f, 0.76f, 0.08f), 0.38f),
                    new GradientColorKey(new Color(0.18f, 0.92f, 0.42f), 1.00f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
            );
            return g;
        }

        // ── References ─────────────────────────────────────────────────
        Image            _hp1Fill,   _hp2Fill;
        RectTransform    _hp1Rect,   _hp2Rect;
        // ダメージトレイル（削れた分が白く残り、遅れて減る）
        RectTransform    _hp1TrailRect, _hp2TrailRect;
        float            _hpT1 = 1f, _hpT2 = 1f;       // 現在のHP割合
        float            _trail1 = 1f, _trail2 = 1f;   // トレイル表示割合
        const float      TrailDrainPerSec = 0.45f;     // トレイルが減る速さ（割合/秒）
        static readonly Color TrailCol = new Color(1f, 0.93f, 0.78f, 0.92f);
        Image            _grd1Fill,  _grd2Fill;
        RectTransform    _grd1Rect,  _grd2Rect;
        TextMeshProUGUI  _hp1Num,    _hp2Num;
        TextMeshProUGUI  _hp1Name,   _hp2Name;
        TextMeshProUGUI  _roundDots1, _roundDots2;
        TextMeshProUGUI  _timerText;
        GameObject       _hudRoot;

        Fighter       _f1, _f2;

        // 協力モードのボスHPバー
        Fighter          _boss;
        GameObject       _bossBarRoot;
        Image            _bossHpFill;
        RectTransform    _bossHpRect;
        TextMeshProUGUI  _bossHpNum;
        static readonly Color BossCol = new Color(1f, 0.3f, 0.3f);

        // 画面下端のバフ・デバフ表示（プレイヤーごと）
        RectTransform _buffRoot1, _buffRoot2;
        readonly System.Collections.Generic.List<BuffChip> _chips1 = new System.Collections.Generic.List<BuffChip>();
        readonly System.Collections.Generic.List<BuffChip> _chips2 = new System.Collections.Generic.List<BuffChip>();
        readonly System.Collections.Generic.List<StatusChip> _chipBuf = new System.Collections.Generic.List<StatusChip>();
        const float ChipW = 116f, ChipH = 30f, ChipGap = 6f, ChipMaxSec = 12f;
        static readonly Color BuffCol   = new Color(0.20f, 0.85f, 0.42f);
        static readonly Color DebuffCol = new Color(0.95f, 0.38f, 0.32f);
        class BuffChip
        {
            public GameObject Root; public RectTransform Rt, FillRt;
            public Image Fill; public TextMeshProUGUI Label;
        }

        // ── Init ───────────────────────────────────────────────────────
        void Start()
        {
            var bm = BattleManager.Instance;
            _f1   = bm?.fighter1;
            _f2   = bm?.fighter2;
            _boss = bm?.boss;

            BuildHUD();
            HideHUD();

            if (_f1 != null) { _f1.OnHPChanged    += (h, m) => UpdateHP(1, h, m);    _f1.OnGuardChanged += (g, m) => UpdateGuard(1, g, m); }
            if (_f2 != null) { _f2.OnHPChanged    += (h, m) => UpdateHP(2, h, m);    _f2.OnGuardChanged += (g, m) => UpdateGuard(2, g, m); }
            if (_boss != null) _boss.OnHPChanged   += (h, m) => UpdateBossHP(h, m);

            if (bm != null)
            {
                bm.OnTimerChanged    += OnTimer;
                bm.OnBattleStart     += ShowHUD;
                bm.OnBattleStart     += RefreshAll;
                bm.OnBattleEnd       += _ => HideHUD();
                bm.OnReturnedToSetup += HideHUD;
                bm.OnTrainingStart   += ShowHUD;
                bm.OnTrainingStart   += RefreshAll;
                bm.OnRoundEnd        += (_, p1, p2) => UpdateRoundDots(p1, p2);
            }
        }

        void OnTimer(float t)
        {
            if (!_timerText) return;
            _timerText.text  = Mathf.CeilToInt(t).ToString();
            _timerText.color = t <= 30f ? UrgentCol : TextWht;
            // 残り10秒は鼓動するように拡縮して切迫感を出す
            _timerText.transform.localScale = t <= 10f && t > 0f
                ? Vector3.one * (1f + 0.16f * Mathf.Abs(Mathf.Sin(Time.time * 6f)))
                : Vector3.one;
        }

        void Update()
        {
            TickTrail(ref _trail1, _hpT1, _hp1TrailRect, true);
            TickTrail(ref _trail2, _hpT2, _hp2TrailRect, false);
            RefreshBuffChips();
        }

        static void TickTrail(ref float trail, float target, RectTransform rect, bool isP1)
        {
            if (rect == null) return;
            if (trail < target) trail = target; // 回復時は即追従
            else if (trail > target) trail = Mathf.MoveTowards(trail, target, Time.deltaTime * TrailDrainPerSec);
            SetBarAnchors(rect, trail, isP1);
        }

        // HP/トレイルバー共通のアンカー設定（1Pは左から、2Pは右から伸びる）
        static void SetBarAnchors(RectTransform rect, float t, bool fromLeft)
        {
            rect.anchorMin = fromLeft ? Vector2.zero       : new Vector2(1f - t, 0f);
            rect.anchorMax = fromLeft ? new Vector2(t, 1f) : Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        void ShowHUD() { if (_hudRoot) _hudRoot.SetActive(true); }
        void HideHUD() { if (_hudRoot) _hudRoot.SetActive(false); }

        void RefreshAll()
        {
            if (_f1 != null) { UpdateHP(1, _f1.CurrentHP, _f1.maxHP); UpdateGuard(1, _f1.CurrentGuardDurability, _f1.maxGuardDurability); }
            if (_f2 != null) { UpdateHP(2, _f2.CurrentHP, _f2.maxHP); UpdateGuard(2, _f2.CurrentGuardDurability, _f2.maxGuardDurability); }

            var bm = BattleManager.Instance;
            if (bm?.Character1 != null && _hp1Name) _hp1Name.text = bm.Character1.characterName;
            if (bm?.Character2 != null && _hp2Name) _hp2Name.text = bm.Character2.characterName;
            if (_timerText) _timerText.text = Mathf.CeilToInt(bm?.TimeRemaining ?? 0f).ToString();
            bool showDots = bm?.bestOf3 == true;
            if (_roundDots1) { _roundDots1.gameObject.SetActive(showDots); UpdateRoundDots(bm?.P1RoundWins ?? 0, bm?.P2RoundWins ?? 0); }
            if (_roundDots2) _roundDots2.gameObject.SetActive(showDots);

            bool coop = bm?.Mode == BattleMode.CoopVsBoss;
            if (_bossBarRoot) _bossBarRoot.SetActive(coop);
            if (coop && _boss != null) UpdateBossHP(_boss.CurrentHP, _boss.maxHP);
        }

        void UpdateHP(int player, float hp, float max)
        {
            var img  = player == 1 ? _hp1Fill : _hp2Fill;
            var rect = player == 1 ? _hp1Rect  : _hp2Rect;
            var num  = player == 1 ? _hp1Num   : _hp2Num;
            if (img == null) return;
            float t = max > 0f ? Mathf.Clamp01(hp / max) : 0f;
            img.color = HpGrad.Evaluate(t);
            if (rect != null) SetBarAnchors(rect, t, player == 1);
            if (player == 1) { _hpT1 = t; if (_trail1 < t) _trail1 = t; }
            else             { _hpT2 = t; if (_trail2 < t) _trail2 = t; }
            if (num) num.text = Mathf.CeilToInt(hp).ToString();
        }

        void UpdateGuard(int player, float guard, float max)
        {
            var img  = player == 1 ? _grd1Fill : _grd2Fill;
            var rect = player == 1 ? _grd1Rect  : _grd2Rect;
            if (img == null || rect == null) return;
            float t = max > 0f ? Mathf.Clamp01(guard / max) : 0f;
            rect.anchorMin = player == 1 ? Vector2.zero          : new Vector2(1f - t, 0f);
            rect.anchorMax = player == 1 ? new Vector2(t, 1f)    : Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            img.color = Color.Lerp(GuardLow, GuardFull, t);
        }

        void UpdateRoundDots(int p1wins, int p2wins)
        {
            if (_roundDots1) _roundDots1.text = RoundDots(p1wins, P1Col);
            if (_roundDots2) _roundDots2.text = RoundDots(p2wins, P2Col);
        }

        static string RoundDots(int wins, Color col)
        {
            string hex = ColorUtility.ToHtmlStringRGB(col);
            string on  = $"<color=#{hex}>●</color>";
            string off = "<color=#556688>○</color>";
            return (wins > 0 ? on : off) + (wins > 1 ? on : off);
        }

        // ── Build ──────────────────────────────────────────────────────
        void BuildHUD()
        {
            _hudRoot = MakeUI("HUDRoot", transform);
            FillParent(_hudRoot);
            BuildTopBar();
            BuildBossBar();
            BuildBuffBars();
        }

        // 画面下端にプレイヤーごとのバフ・デバフ枠を用意（実況バーの上）。
        void BuildBuffBars()
        {
            _buffRoot1 = MakeBuffRoot("BuffRow1P", true);
            _buffRoot2 = MakeBuffRoot("BuffRow2P", false);
        }

        RectTransform MakeBuffRoot(string name, bool isP1)
        {
            var go = MakeUI(name, _hudRoot.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(isP1 ? 0f : 1f, 0f);
            rt.anchorMax = new Vector2(isP1 ? 0f : 1f, 0f);
            rt.pivot     = new Vector2(isP1 ? 0f : 1f, 0f);
            rt.anchoredPosition = new Vector2(isP1 ? 18f : -18f, 110f); // 実況バー(100px)の上
            rt.sizeDelta = new Vector2(0f, ChipH);
            return rt;
        }

        BuffChip CreateChip(RectTransform root, bool isP1)
        {
            var go = MakeUI("Chip", root);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(isP1 ? 0f : 1f, 0f);
            rt.pivot = new Vector2(isP1 ? 0f : 1f, 0f);
            rt.sizeDelta = new Vector2(ChipW, ChipH);
            go.AddComponent<Image>().color = new Color(0.04f, 0.05f, 0.08f, 0.92f);

            // 残り時間で減るフィル
            var fillGo = MakeUI("Fill", go.transform);
            var frt = fillGo.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            var fill = fillGo.AddComponent<Image>();
            fill.raycastTarget = false;

            var labGo = MakeUI("Lbl", go.transform);
            FillParent(labGo);
            var lab = labGo.AddComponent<TextMeshProUGUI>();
            lab.fontSize = 15f; lab.fontStyle = FontStyles.Bold;
            lab.alignment = TextAlignmentOptions.Center; lab.color = Color.white;
            lab.textWrappingMode = TextWrappingModes.NoWrap; lab.raycastTarget = false;
            UITheme.Apply(lab);

            return new BuffChip { Root = go, Rt = rt, FillRt = frt, Fill = fill, Label = lab };
        }

        void RefreshBuffChips()
        {
            RefreshBuffSide(_f1, _buffRoot1, _chips1, true);
            RefreshBuffSide(_f2, _buffRoot2, _chips2, false);
        }

        void RefreshBuffSide(Fighter f, RectTransform root, System.Collections.Generic.List<BuffChip> pool, bool isP1)
        {
            if (root == null) return;
            _chipBuf.Clear();
            if (f != null && f.State != FighterState.Dead) f.CollectStatusChips(_chipBuf);

            for (int i = 0; i < _chipBuf.Count; i++)
            {
                if (i >= pool.Count) pool.Add(CreateChip(root, isP1));
                var chip = pool[i];
                chip.Root.SetActive(true);
                var sc = _chipBuf[i];
                chip.Rt.anchoredPosition = new Vector2((isP1 ? 1f : -1f) * i * (ChipW + ChipGap), 0f);

                bool perm = sc.Remaining < 0f;
                float frac = perm ? 1f : Mathf.Clamp01(sc.Remaining / ChipMaxSec);
                chip.FillRt.anchorMax = new Vector2(frac, 1f);
                chip.FillRt.offsetMax = Vector2.zero;
                Color c = sc.IsBuff ? BuffCol : DebuffCol;
                chip.Fill.color = new Color(c.r, c.g, c.b, perm ? 0.6f : 0.5f);
                chip.Label.text = perm ? $"{sc.Label} ∞" : $"{sc.Label} {sc.Remaining:0.0}";
            }
            for (int i = _chipBuf.Count; i < pool.Count; i++)
                if (pool[i].Root.activeSelf) pool[i].Root.SetActive(false);
        }

        // 協力モードのボスHPバー（画面上部中央、トップバーの下）。Versusでは非表示。
        void BuildBossBar()
        {
            const float TOTAL_H = 92f + 5f * 2f; // トップバー高（BuildTopBar基準）
            const float BAR_H   = 34f;
            const float W_FRAC  = 0.6f;          // 画面幅に対するバー幅の割合
            const float NAME_H  = 22f;

            _bossBarRoot = MakeUI("BossBar", _hudRoot.transform);
            var rt = _bossBarRoot.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2((1f - W_FRAC) * 0.5f, 1f);
            rt.anchorMax = new Vector2((1f + W_FRAC) * 0.5f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -(TOTAL_H + 6f + NAME_H + BAR_H));
            rt.offsetMax = new Vector2(0f, -(TOTAL_H + 6f));

            // ボス名ラベル
            var nameGo = MakeUI("BossName", _bossBarRoot.transform);
            Anch(nameGo, 0,1,1,1,  0,-NAME_H,0,0);
            var nm = nameGo.AddComponent<TextMeshProUGUI>();
            nm.text = "BOSS"; nm.fontSize = 18f;
            nm.fontStyle = FontStyles.Bold | FontStyles.Italic;
            nm.alignment = TextAlignmentOptions.Center; nm.color = BossCol;
            UITheme.Apply(nm);

            // バー背景
            var barBg = MakeUI("BossBarBg", _bossBarRoot.transform);
            Anch(barBg, 0,0,1,1,  0,0,0,-NAME_H);
            barBg.AddComponent<Image>().color = BgBar;

            // 上端アクセント
            var accent = MakeUI("BossAccent", barBg.transform);
            Anch(accent, 0,1,1,1,  0,-2f,0,0);
            accent.AddComponent<Image>().color = BossCol;

            // HPフィル（中央から両側に減る：右から減らす単純実装）
            var fillGo = MakeUI("BossHPFill", barBg.transform);
            FillParent(fillGo);
            var fi = fillGo.AddComponent<Image>();
            fi.sprite = UITheme.VGradient;
            fi.color  = HpGrad.Evaluate(1f);
            _bossHpFill = fi; _bossHpRect = fillGo.GetComponent<RectTransform>();

            // HP数値
            var numGo = MakeUI("BossHPNum", barBg.transform);
            FillParent(numGo);
            var num = numGo.AddComponent<TextMeshProUGUI>();
            num.text = "300"; num.fontSize = 22f;
            num.fontStyle = FontStyles.Bold | FontStyles.Italic;
            num.alignment = TextAlignmentOptions.Center; num.color = TextWht;
            UITheme.Apply(num);
            _bossHpNum = num;
        }

        void UpdateBossHP(float hp, float max)
        {
            if (_bossHpFill == null) return;
            float t = max > 0f ? Mathf.Clamp01(hp / max) : 0f;
            _bossHpFill.color = HpGrad.Evaluate(t);
            if (_bossHpRect != null)
            {
                // 中央から両側へ縮む
                _bossHpRect.anchorMin = new Vector2((1f - t) * 0.5f, 0f);
                _bossHpRect.anchorMax = new Vector2((1f + t) * 0.5f, 1f);
                _bossHpRect.offsetMin = _bossHpRect.offsetMax = Vector2.zero;
            }
            if (_bossHpNum) _bossHpNum.text = Mathf.CeilToInt(hp).ToString();
        }

        void BuildTopBar()
        {
            const float ZH      = 92f;   // HP zone height
            const float TIMER_W = 136f;  // timer width
            const float PAD     = 5f;    // outer margin
            const float GAP     = 2f;    // zone–timer gap
            const float TOTAL_H = ZH + PAD * 2f;

            // ─ full-width dark backdrop ─────────────────────────────
            var backdrop = MakeUI("TopBackdrop", _hudRoot.transform);
            Anch(backdrop, 0,1, 1,1,  0,-TOTAL_H, 0,0);
            backdrop.AddComponent<Image>().color = BgDeep;

            // ─ 1px top glow line ────────────────────────────────────
            var topGlow = MakeUI("TopGlow", _hudRoot.transform);
            Anch(topGlow, 0,1, 1,1,  0,-1f, 0,0);
            topGlow.AddComponent<Image>().color = GlowLine;

            // ─ P1 HP zone (left of timer) ───────────────────────────
            var z1 = MakeUI("Zone1P", _hudRoot.transform);
            Anch(z1, 0f,1f, 0.5f,1f,  PAD, -TOTAL_H+PAD, -(TIMER_W*0.5f+GAP), -PAD);
            BuildHPZone(z1.transform, true,
                out _hp1Fill, out _hp1Rect,
                out _grd1Fill, out _grd1Rect,
                out _hp1Num, out _hp1Name);

            // ─ P2 HP zone (right of timer) ──────────────────────────
            var z2 = MakeUI("Zone2P", _hudRoot.transform);
            Anch(z2, 0.5f,1f, 1f,1f,  TIMER_W*0.5f+GAP, -TOTAL_H+PAD, -PAD, -PAD);
            BuildHPZone(z2.transform, false,
                out _hp2Fill, out _hp2Rect,
                out _grd2Fill, out _grd2Rect,
                out _hp2Num, out _hp2Name);

            // ─ Round win dots ────────────────────────────────────────
            var rd1Go = MakeUI("RoundDots1", _hudRoot.transform);
            Anch(rd1Go, 0f,1f, 0.5f,1f,  PAD+4f, -TOTAL_H+1f, -(TIMER_W*0.5f+GAP), -TOTAL_H+17f);
            _roundDots1 = rd1Go.AddComponent<TextMeshProUGUI>();
            _roundDots1.text = RoundDots(0, P1Col);
            _roundDots1.fontSize = 18f;
            _roundDots1.alignment = TextAlignmentOptions.Left;
            _roundDots1.color = Color.white;
            UITheme.Apply(_roundDots1);

            var rd2Go = MakeUI("RoundDots2", _hudRoot.transform);
            Anch(rd2Go, 0.5f,1f, 1f,1f,  TIMER_W*0.5f+GAP, -TOTAL_H+1f, -PAD-4f, -TOTAL_H+17f);
            _roundDots2 = rd2Go.AddComponent<TextMeshProUGUI>();
            _roundDots2.text = RoundDots(0, P2Col);
            _roundDots2.fontSize = 18f;
            _roundDots2.alignment = TextAlignmentOptions.Right;
            _roundDots2.color = Color.white;
            UITheme.Apply(_roundDots2);

            // ─ Timer ────────────────────────────────────────────────
            var tc = MakeUI("TimerBox", _hudRoot.transform);
            var tcRt = tc.GetComponent<RectTransform>();
            tcRt.anchorMin = new Vector2(0.5f, 1f);
            tcRt.anchorMax = new Vector2(0.5f, 1f);
            tcRt.sizeDelta = new Vector2(TIMER_W, TOTAL_H);
            tcRt.anchoredPosition = new Vector2(0f, -TOTAL_H * 0.5f);
            tc.AddComponent<Image>().color = UITheme.SteelDark;

            // timer border lines (ゴールド枠 / 上下を2pxで強調)
            AddLine(tc.transform, "BdT", 0,1,1,1,   0,-2f,0,0,     TimerEdge);
            AddLine(tc.transform, "BdB", 0,0,1,0,   0, 0f,0,2f,    TimerEdge);
            AddLine(tc.transform, "BdL", 0,0,0,1,   0, 0f,1f,0,    new Color(TimerEdge.r,TimerEdge.g,TimerEdge.b,0.5f));
            AddLine(tc.transform, "BdR", 1,0,1,1,  -1f,0f,0,0,     new Color(TimerEdge.r,TimerEdge.g,TimerEdge.b,0.5f));

            // "TIME" sublabel
            var tlGo = MakeUI("TLabel", tc.transform);
            Anch(tlGo, 0,1, 1,1,  2,-18f,-2,0);
            var tl = tlGo.AddComponent<TextMeshProUGUI>();
            tl.text = "TIME"; tl.fontSize = 12f;
            tl.alignment = TextAlignmentOptions.Center; tl.color = TextDim;
            UITheme.Apply(tl);

            // main timer number
            var tnGo = MakeUI("TimerNum", tc.transform);
            Anch(tnGo, 0,0, 1,1,  2,2,-2,-20f);
            _timerText = tnGo.AddComponent<TextMeshProUGUI>();
            _timerText.text = "99"; _timerText.fontSize = 64f;
            _timerText.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _timerText.alignment = TextAlignmentOptions.Center;
            _timerText.color = TextWht;
            UITheme.Apply(_timerText);
        }

        void BuildHPZone(Transform parent, bool isP1,
            out Image hpFill, out RectTransform hpRect,
            out Image grdFill, out RectTransform grdRect,
            out TextMeshProUGUI hpNum, out TextMeshProUGUI nameLabel)
        {
            // Layout inside zone (height = 64px):
            //  top 2px      : accent line (player color)
            //  top 2+5=7    : inner pad starts
            //  top 7..24    : name row (17px)
            //  top 24..52   : HP bar  (28px, with 1px gap at edges)
            //  bot 5..12    : guard bar (7px)
            //  bot 0..5     : inner bottom pad
            //  inner sides  : stripe(3px) + pad(5px)
            //  inner numW   : 58px at inner edge

            const float STRIPE = 4f;
            const float IPAD   = 6f;
            const float NUM_W  = 104f;
            const float NAME_H = 24f;
            const float GUARD_H= 10f;
            Color pCol = isP1 ? P1Col : P2Col;
            float slant = isP1 ? Slant : -Slant;

            // bg (メタリックなスチール地)
            parent.gameObject.AddComponent<Image>().color = BgMid;

            // 上端ベベルハイライト
            var bevelTop = MakeUI("Bevel", parent);
            Anch(bevelTop, 0,1,1,1,  0,-3f,0,-1f);
            bevelTop.AddComponent<Image>().color = Bevel;

            // top accent line (3px, player color)
            var accent = MakeUI("Accent", parent);
            Anch(accent, 0,1,1,1,  0,-3f,0,0);
            accent.AddComponent<Image>().color = pCol;

            // outer vertical stripe (スラント付きネオン)
            var stripe = MakeUI("Stripe", parent);
            if (isP1) Anch(stripe, 0,0,0,1,  0,0, STRIPE,0);
            else      Anch(stripe, 1,0,1,1,  -STRIPE,0, 0,0);
            var stripeImg = stripe.AddComponent<Image>();
            stripeImg.color = pCol;
            UITheme.Skew(stripeImg, slant);

            // name label
            float nameL = isP1 ? STRIPE + IPAD : NUM_W + IPAD * 2f;
            float nameR = isP1 ? NUM_W + IPAD * 2f : STRIPE + IPAD;
            var nameGo = MakeUI("Name", parent);
            Anch(nameGo, 0,1,1,1,  nameL, -(2f+IPAD+NAME_H), -nameR, -(2f+IPAD));
            var nmTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nmTmp.text = isP1 ? "1P" : "2P";
            nmTmp.fontSize = 19f; nmTmp.fontStyle = FontStyles.Bold | FontStyles.Italic;
            nmTmp.alignment = isP1 ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
            nmTmp.color = pCol;
            nmTmp.textWrappingMode = TextWrappingModes.NoWrap;
            nmTmp.overflowMode = TextOverflowModes.Ellipsis;
            UITheme.Apply(nmTmp);
            nameLabel = nmTmp;

            // HP number (inner edge, big)
            var numGo = MakeUI("HPNum", parent);
            if (isP1) Anch(numGo, 1,0,1,1, -(NUM_W+IPAD), GUARD_H+IPAD, -IPAD, -(2f+IPAD));
            else      Anch(numGo, 0,0,0,1,  IPAD, GUARD_H+IPAD, NUM_W+IPAD, -(2f+IPAD));
            var numTmp = numGo.AddComponent<TextMeshProUGUI>();
            numTmp.text = "300"; numTmp.fontSize = 40f;
            numTmp.fontStyle = FontStyles.Bold | FontStyles.Italic;
            numTmp.alignment = isP1 ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
            numTmp.color = TextWht;
            numTmp.textWrappingMode = TextWrappingModes.NoWrap;
            numTmp.overflowMode = TextOverflowModes.Overflow;
            UITheme.Apply(numTmp);
            hpNum = numTmp;

            // HP bar background
            float barL = isP1 ? STRIPE + IPAD : NUM_W + IPAD * 2f;
            float barR = isP1 ? NUM_W + IPAD * 2f : STRIPE + IPAD;
            var barBg = MakeUI("BarBg", parent);
            Anch(barBg, 0,0,1,1,  barL, GUARD_H+IPAD, -barR, -(2f+IPAD+NAME_H+2f));
            var barBgImg = barBg.AddComponent<Image>();
            barBgImg.color = BgBar;
            UITheme.Skew(barBgImg, slant);

            // ダメージトレイル（HPフィルの背面。削れた分が白く残って遅れて減る）
            var trailGo = MakeUI("HPTrail", barBg.transform);
            FillParent(trailGo);
            var trailImg = trailGo.AddComponent<Image>();
            trailImg.color = TrailCol;
            UITheme.Skew(trailImg, slant);
            if (isP1) _hp1TrailRect = trailGo.GetComponent<RectTransform>();
            else      _hp2TrailRect = trailGo.GetComponent<RectTransform>();

            // HP fill (メタリックグラデ + スラント)
            var fillGo = MakeUI("HPFill", barBg.transform);
            FillParent(fillGo);
            var fi = fillGo.AddComponent<Image>();
            fi.sprite = UITheme.VGradient;
            fi.color = HpGrad.Evaluate(1f);
            UITheme.Skew(fi, slant);
            hpFill = fi; hpRect = fillGo.GetComponent<RectTransform>();

            // バー枠ネオンライン(上下)
            var barTop = MakeUI("BarT", barBg.transform);
            Anch(barTop, 0,1,1,1, 0,-2f,0,0);
            var barTopImg = barTop.AddComponent<Image>();
            barTopImg.color = new Color(pCol.r, pCol.g, pCol.b, 0.5f);
            UITheme.Skew(barTopImg, slant);

            // Guard bar background
            var grdBg = MakeUI("GrdBg", parent);
            Anch(grdBg, 0,0,1,0,  barL, IPAD, -barR, IPAD+GUARD_H);
            var grdBgImg = grdBg.AddComponent<Image>();
            grdBgImg.color = BgBar;
            UITheme.Skew(grdBgImg, slant);

            // Guard fill
            var grdGo = MakeUI("GrdFill", grdBg.transform);
            FillParent(grdGo);
            var gi = grdGo.AddComponent<Image>();
            gi.sprite = UITheme.VGradient;
            gi.color = GuardFull;
            UITheme.Skew(gi, slant);
            grdFill = gi; grdRect = grdGo.GetComponent<RectTransform>();
        }

        // ── Helpers ────────────────────────────────────────────────────
        static GameObject MakeUI(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = Vector2.zero;
            return go;
        }

        static void FillParent(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // axMin/ayMin = anchorMin, axMax/ayMax = anchorMax
        // oxMin/oyMin = offsetMin (bottom-left), oxMax/oyMax = offsetMax (top-right)
        static void Anch(GameObject go,
            float axMin, float ayMin, float axMax, float ayMax,
            float oxMin, float oyMin, float oxMax, float oyMax)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(axMin, ayMin); rt.anchorMax = new Vector2(axMax, ayMax);
            rt.offsetMin = new Vector2(oxMin, oyMin); rt.offsetMax = new Vector2(oxMax, oyMax);
        }

        static void AddLine(Transform parent, string name,
            float axMin, float ayMin, float axMax, float ayMax,
            float oxMin, float oyMin, float oxMax, float oyMax, Color col)
        {
            var go = MakeUI(name, parent);
            Anch(go, axMin,ayMin, axMax,ayMax, oxMin,oyMin, oxMax,oyMax);
            go.AddComponent<Image>().color = col;
        }
    }
}
