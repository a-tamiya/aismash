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
        // ── Palette ────────────────────────────────────────────────────
        static readonly Color BgDeep    = new Color(0.01f, 0.03f, 0.07f, 0.93f);
        static readonly Color BgMid     = new Color(0.03f, 0.06f, 0.12f, 0.96f);
        static readonly Color BgBar     = new Color(0.01f, 0.02f, 0.05f, 1.00f);
        static readonly Color BgSlot    = new Color(0.02f, 0.04f, 0.09f, 0.97f);
        static readonly Color P1Col     = new Color(0.12f, 0.62f, 1.00f);
        static readonly Color P2Col     = new Color(1.00f, 0.20f, 0.20f);
        static readonly Color TextWht   = Color.white;
        static readonly Color TextDim   = new Color(0.50f, 0.65f, 0.82f);
        static readonly Color GuardFull = new Color(0.28f, 0.72f, 1.00f);
        static readonly Color GuardLow  = new Color(0.85f, 0.18f, 0.92f);
        static readonly Color TimerEdge = new Color(0.22f, 0.55f, 1.00f, 0.60f);
        static readonly Color GlowLine  = new Color(0.18f, 0.50f, 1.00f, 0.35f);
        static readonly Color UrgentCol = new Color(1.00f, 0.22f, 0.18f);

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
        Image            _grd1Fill,  _grd2Fill;
        RectTransform    _grd1Rect,  _grd2Rect;
        TextMeshProUGUI  _hp1Num,    _hp2Num;
        TextMeshProUGUI  _hp1Name,   _hp2Name;
        TextMeshProUGUI  _timerText;
        GameObject       _hudRoot;

        readonly TextMeshProUGUI[] _p1Names = new TextMeshProUGUI[4];
        readonly TextMeshProUGUI[] _p2Names = new TextMeshProUGUI[4];

        Fighter       _f1, _f2;
        SkillExecutor _se1, _se2;

        static readonly string[] SlotJp    = { "基本技A", "基本技B", "基本技C", "スマッシュ" };
        static readonly string[] Keys1P    = { "J", "K", "L", "A+J" };
        static readonly string[] Keys2P    = { "テン2", "テン3", "テン1", "←+2" };
        static readonly Color[]  SlotCols  = {
            new Color(1.00f, 0.28f, 0.28f),
            new Color(0.28f, 0.58f, 1.00f),
            new Color(0.20f, 0.90f, 0.48f),
            new Color(1.00f, 0.76f, 0.08f),
        };

        // ── Init ───────────────────────────────────────────────────────
        void Start()
        {
            var bm = BattleManager.Instance;
            _f1  = bm?.fighter1;
            _f2  = bm?.fighter2;
            _se1 = _f1?.GetComponent<SkillExecutor>();
            _se2 = _f2?.GetComponent<SkillExecutor>();

            BuildHUD();
            HideHUD();

            if (_f1 != null) { _f1.OnHPChanged    += (h, m) => UpdateHP(1, h, m);    _f1.OnGuardChanged += (g, m) => UpdateGuard(1, g, m); }
            if (_f2 != null) { _f2.OnHPChanged    += (h, m) => UpdateHP(2, h, m);    _f2.OnGuardChanged += (g, m) => UpdateGuard(2, g, m); }

            if (bm != null)
            {
                bm.OnTimerChanged    += OnTimer;
                bm.OnBattleStart     += ShowHUD;
                bm.OnBattleStart     += RefreshAll;
                bm.OnBattleEnd       += _ => HideHUD();
                bm.OnReturnedToSetup += HideHUD;
                bm.OnTrainingStart   += ShowHUD;
                bm.OnTrainingStart   += RefreshAll;
            }
        }

        void OnTimer(float t)
        {
            if (!_timerText) return;
            _timerText.text  = Mathf.CeilToInt(t).ToString();
            _timerText.color = t <= 30f ? UrgentCol : TextWht;
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
            RefreshSkillNames(_p1Names, _se1);
            RefreshSkillNames(_p2Names, _se2);
            if (_timerText) _timerText.text = Mathf.CeilToInt(bm?.TimeRemaining ?? 0f).ToString();
        }

        void RefreshSkillNames(TextMeshProUGUI[] labels, SkillExecutor se)
        {
            for (int i = 0; i < 4; i++)
            {
                if (labels[i] == null || se == null) continue;
                var sk = se.GetSkill((SkillSlot)i);
                labels[i].text = sk?.skill_name ?? SlotJp[i];
            }
        }

        void UpdateHP(int player, float hp, float max)
        {
            var img  = player == 1 ? _hp1Fill : _hp2Fill;
            var rect = player == 1 ? _hp1Rect  : _hp2Rect;
            var num  = player == 1 ? _hp1Num   : _hp2Num;
            if (img == null) return;
            float t = max > 0f ? Mathf.Clamp01(hp / max) : 0f;
            img.color = HpGrad.Evaluate(t);
            if (rect != null)
            {
                rect.anchorMin = player == 1 ? Vector2.zero          : new Vector2(1f - t, 0f);
                rect.anchorMax = player == 1 ? new Vector2(t, 1f)    : Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
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

        // ── Build ──────────────────────────────────────────────────────
        void BuildHUD()
        {
            _hudRoot = MakeUI("HUDRoot", transform);
            FillParent(_hudRoot);
            BuildTopBar();
        }

        void BuildTopBar()
        {
            const float ZH      = 64f;   // HP zone height
            const float TIMER_W = 108f;  // timer width
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

            // ─ Timer ────────────────────────────────────────────────
            var tc = MakeUI("TimerBox", _hudRoot.transform);
            var tcRt = tc.GetComponent<RectTransform>();
            tcRt.anchorMin = new Vector2(0.5f, 1f);
            tcRt.anchorMax = new Vector2(0.5f, 1f);
            tcRt.sizeDelta = new Vector2(TIMER_W, TOTAL_H);
            tcRt.anchoredPosition = new Vector2(0f, -TOTAL_H * 0.5f);
            tc.AddComponent<Image>().color = new Color(0.01f, 0.03f, 0.08f, 1f);

            // timer border lines
            AddLine(tc.transform, "BdT", 0,1,1,1,   0,-1f,0,0,     TimerEdge);
            AddLine(tc.transform, "BdB", 0,0,1,0,   0, 0f,0,1f,    TimerEdge);
            AddLine(tc.transform, "BdL", 0,0,0,1,   0, 0f,1f,0,    TimerEdge);
            AddLine(tc.transform, "BdR", 1,0,1,1,  -1f,0f,0,0,     TimerEdge);

            // "TIME" sublabel
            var tlGo = MakeUI("TLabel", tc.transform);
            Anch(tlGo, 0,1, 1,1,  2,-14f,-2,0);
            var tl = tlGo.AddComponent<TextMeshProUGUI>();
            tl.text = "TIME"; tl.fontSize = 9f;
            tl.alignment = TextAlignmentOptions.Center; tl.color = TextDim;
            UITheme.Apply(tl);

            // main timer number
            var tnGo = MakeUI("TimerNum", tc.transform);
            Anch(tnGo, 0,0, 1,1,  2,2,-2,-16f);
            _timerText = tnGo.AddComponent<TextMeshProUGUI>();
            _timerText.text = "99"; _timerText.fontSize = 50f;
            _timerText.fontStyle = FontStyles.Bold;
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

            const float STRIPE = 3f;
            const float IPAD   = 5f;
            const float NUM_W  = 58f;
            const float NAME_H = 17f;
            const float GUARD_H= 7f;
            Color pCol = isP1 ? P1Col : P2Col;

            // bg
            parent.gameObject.AddComponent<Image>().color = BgMid;

            // top accent line (2px, player color)
            var accent = MakeUI("Accent", parent);
            Anch(accent, 0,1,1,1,  0,-2f,0,0);
            accent.AddComponent<Image>().color = pCol;

            // outer vertical stripe
            var stripe = MakeUI("Stripe", parent);
            if (isP1) Anch(stripe, 0,0,0,1,  0,0, STRIPE,0);
            else      Anch(stripe, 1,0,1,1,  -STRIPE,0, 0,0);
            stripe.AddComponent<Image>().color = pCol;

            // name label
            float nameL = isP1 ? STRIPE + IPAD : NUM_W + IPAD * 2f;
            float nameR = isP1 ? NUM_W + IPAD * 2f : STRIPE + IPAD;
            var nameGo = MakeUI("Name", parent);
            Anch(nameGo, 0,1,1,1,  nameL, -(2f+IPAD+NAME_H), -nameR, -(2f+IPAD));
            var nmTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nmTmp.text = isP1 ? "1P" : "2P";
            nmTmp.fontSize = 13f; nmTmp.fontStyle = FontStyles.Bold;
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
            numTmp.text = "300"; numTmp.fontSize = 30f;
            numTmp.fontStyle = FontStyles.Bold;
            numTmp.alignment = isP1 ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
            numTmp.color = TextWht;
            UITheme.Apply(numTmp);
            hpNum = numTmp;

            // HP bar background
            float barL = isP1 ? STRIPE + IPAD : NUM_W + IPAD * 2f;
            float barR = isP1 ? NUM_W + IPAD * 2f : STRIPE + IPAD;
            var barBg = MakeUI("BarBg", parent);
            Anch(barBg, 0,0,1,1,  barL, GUARD_H+IPAD, -barR, -(2f+IPAD+NAME_H+2f));
            barBg.AddComponent<Image>().color = BgBar;

            // HP fill
            var fillGo = MakeUI("HPFill", barBg.transform);
            FillParent(fillGo);
            var fi = fillGo.AddComponent<Image>();
            fi.color = HpGrad.Evaluate(1f);
            hpFill = fi; hpRect = fillGo.GetComponent<RectTransform>();

            // subtle inner border on bar
            AddLine(barBg.transform, "BarT", 0,1,1,1, 0,-1f,0,0, new Color(1,1,1,0.12f));

            // Guard bar background
            var grdBg = MakeUI("GrdBg", parent);
            Anch(grdBg, 0,0,1,0,  barL, IPAD, -barR, IPAD+GUARD_H);
            grdBg.AddComponent<Image>().color = BgBar;

            // Guard fill
            var grdGo = MakeUI("GrdFill", grdBg.transform);
            FillParent(grdGo);
            var gi = grdGo.AddComponent<Image>();
            gi.color = GuardFull;
            grdFill = gi; grdRect = grdGo.GetComponent<RectTransform>();
        }

        void BuildSkillBars()
        {
            const float SH  = 66f;
            const float PAD = 5f;

            var sb1 = MakeUI("Skills1P", _hudRoot.transform);
            Anch(sb1, 0,0, 0.5f,0,  PAD,PAD, -3f,SH+PAD);
            BuildSlots(sb1.transform, true);

            var sb2 = MakeUI("Skills2P", _hudRoot.transform);
            Anch(sb2, 0.5f,0, 1f,0,  3f,PAD, -PAD,SH+PAD);
            BuildSlots(sb2.transform, false);

            // bottom glow line
            var botLine = MakeUI("BotGlow", _hudRoot.transform);
            Anch(botLine, 0,0,1,0,  0,SH+PAD*2-1f, 0,SH+PAD*2);
            botLine.AddComponent<Image>().color = GlowLine;
        }

        void BuildSlots(Transform parent, bool isP1)
        {
            string[] keys = isP1 ? Keys1P : Keys2P;
            var names     = isP1 ? _p1Names : _p2Names;
            var se        = isP1 ? _se1 : _se2;

            for (int i = 0; i < 4; i++)
            {
                float xMin = i * 0.25f + 0.004f;
                float xMax = (i+1) * 0.25f - 0.004f;

                var slot = MakeUI($"Slot{i}", parent);
                var sRt = slot.GetComponent<RectTransform>();
                sRt.anchorMin = new Vector2(xMin, 0f);
                sRt.anchorMax = new Vector2(xMax, 1f);
                sRt.offsetMin = sRt.offsetMax = Vector2.zero;
                slot.AddComponent<Image>().color = BgSlot;

                Color sc = SlotCols[i];

                // colored top border (3px)
                var topBord = MakeUI("TopB", slot.transform);
                Anch(topBord, 0,1,1,1,  0,-3f,0,0);
                topBord.AddComponent<Image>().color = sc;

                // dim left edge
                var leftB = MakeUI("LeftB", slot.transform);
                Anch(leftB, 0,0,0,1,  0,0,1f,0);
                leftB.AddComponent<Image>().color = new Color(sc.r, sc.g, sc.b, 0.22f);

                // key badge
                var kbGo = MakeUI("KeyBg", slot.transform);
                var kbRt = kbGo.GetComponent<RectTransform>();
                kbRt.anchorMin = kbRt.anchorMax = new Vector2(0.5f, 1f);
                kbRt.sizeDelta = new Vector2(34f, 15f);
                kbRt.anchoredPosition = new Vector2(0f, -12f);
                kbGo.AddComponent<Image>().color = new Color(sc.r, sc.g, sc.b, 0.22f);

                var kbLine = MakeUI("KBLine", kbGo.transform);
                Anch(kbLine, 0,1,1,1, 0,-1f,0,0);
                kbLine.AddComponent<Image>().color = new Color(sc.r, sc.g, sc.b, 0.7f);

                var keyTxt = MakeUI("KeyTxt", kbGo.transform);
                FillParent(keyTxt);
                var kt = keyTxt.AddComponent<TextMeshProUGUI>();
                kt.text = keys[i]; kt.fontSize = 10f; kt.fontStyle = FontStyles.Bold;
                kt.alignment = TextAlignmentOptions.Center; kt.color = sc;
                UITheme.Apply(kt);

                // skill name
                var nmGo = MakeUI("SkillName", slot.transform);
                Anch(nmGo, 0,0.35f,1,1,  5f,0,-5f,-22f);
                var nm = nmGo.AddComponent<TextMeshProUGUI>();
                var sk = se?.GetSkill((SkillSlot)i);
                nm.text = sk?.skill_name ?? SlotJp[i];
                nm.fontSize = 13f; nm.fontStyle = FontStyles.Bold;
                nm.alignment = TextAlignmentOptions.Center; nm.color = TextWht;
                nm.textWrappingMode = TextWrappingModes.NoWrap;
                nm.overflowMode = TextOverflowModes.Ellipsis;
                UITheme.Apply(nm);
                names[i] = nm;

                // slot type label
                var tpGo = MakeUI("SlotType", slot.transform);
                Anch(tpGo, 0,0,1,0.38f,  4f,2f,-4f,0);
                var tp = tpGo.AddComponent<TextMeshProUGUI>();
                tp.text = SlotJp[i]; tp.fontSize = 9f;
                tp.alignment = TextAlignmentOptions.Center; tp.color = TextDim;
                UITheme.Apply(tp);
            }
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
