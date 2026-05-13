using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;

namespace PromptFighters.UI
{
    // バトル中HUD全体をランタイムで構築する。
    // CanvasにアタッチするだけでHPバー・タイマー・スキルバーを生成する。
    [RequireComponent(typeof(Canvas))]
    public class BattleHUD : MonoBehaviour
    {
        // ── 内部参照 ──────────────────────────────────────────────
        Image            _hp1Fill, _hp2Fill;
        RectTransform    _hp1FillRect, _hp2FillRect;
        Image            _guard1Fill, _guard2Fill;
        RectTransform    _guard1FillRect, _guard2FillRect;
        TextMeshProUGUI  _hp1Num, _hp2Num, _hp1Name, _hp2Name;
        TextMeshProUGUI  _timerText;
        GameObject       _hudRoot;

        readonly TextMeshProUGUI[] _p1Names = new TextMeshProUGUI[4];
        readonly TextMeshProUGUI[] _p2Names = new TextMeshProUGUI[4];

        Fighter       _f1, _f2;
        SkillExecutor _se1, _se2;

        static readonly string[] SlotJp   = { "基本技A", "基本技B", "基本技C", "スマッシュ" };
        static readonly string[] Keys1P   = { "J", "K", "L", "U" };
        static readonly string[] Keys2P   = { "テン2", "テン3", "テン1", "テン5" };
        static readonly Color    P1Color  = new Color(0.40f, 0.75f, 1.00f);
        static readonly Color    P2Color  = new Color(1.00f, 0.55f, 0.30f);

        static readonly Gradient HpGrad = BuildHpGradient();

        static Gradient BuildHpGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] {
                    new GradientColorKey(new Color(0.95f, 0.18f, 0.18f), 0f),
                    new GradientColorKey(new Color(1.00f, 0.65f, 0.00f), 0.30f),
                    new GradientColorKey(new Color(0.15f, 0.85f, 0.30f), 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
            );
            return g;
        }

        // ── 初期化 ────────────────────────────────────────────────
        void Start()
        {
            var bm = BattleManager.Instance;
            _f1  = bm?.fighter1;
            _f2  = bm?.fighter2;
            _se1 = _f1?.GetComponent<SkillExecutor>();
            _se2 = _f2?.GetComponent<SkillExecutor>();

            BuildHUD();
            HideHUD();

            if (_f1 != null) _f1.OnHPChanged += (hp, max) => UpdateHP(1, hp, max);
            if (_f2 != null) _f2.OnHPChanged += (hp, max) => UpdateHP(2, hp, max);
            if (_f1 != null) _f1.OnGuardChanged += (guard, max) => UpdateGuard(1, guard, max);
            if (_f2 != null) _f2.OnGuardChanged += (guard, max) => UpdateGuard(2, guard, max);

            if (bm != null)
            {
                bm.OnTimerChanged  += t => { if (_timerText) _timerText.text = Mathf.CeilToInt(t).ToString(); };
                bm.OnBattleStart   += ShowHUD;
                bm.OnBattleStart   += RefreshAll;
                bm.OnBattleEnd     += _ => HideHUD();
                bm.OnReturnedToSetup += HideHUD;
                bm.OnTrainingStart += ShowHUD;
                bm.OnTrainingStart += RefreshAll;
            }
        }

        void ShowHUD() { if (_hudRoot) _hudRoot.SetActive(true); }
        void HideHUD() { if (_hudRoot) _hudRoot.SetActive(false); }

        void RefreshAll()
        {
            if (_f1 != null) UpdateHP(1, _f1.CurrentHP, _f1.maxHP);
            if (_f2 != null) UpdateHP(2, _f2.CurrentHP, _f2.maxHP);
            if (_f1 != null) UpdateGuard(1, _f1.CurrentGuardDurability, _f1.maxGuardDurability);
            if (_f2 != null) UpdateGuard(2, _f2.CurrentGuardDurability, _f2.maxGuardDurability);

            var bm = BattleManager.Instance;
            if (bm?.Character1 != null && _hp1Name) _hp1Name.text = bm.Character1.characterName;
            if (bm?.Character2 != null && _hp2Name) _hp2Name.text = bm.Character2.characterName;

            RefreshSkillNames(_p1Names, _se1);
            RefreshSkillNames(_p2Names, _se2);

            if (_timerText) _timerText.text = Mathf.CeilToInt(BattleManager.Instance?.TimeRemaining ?? 0).ToString();
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
            var fill = player == 1 ? _hp1Fill : _hp2Fill;
            var num  = player == 1 ? _hp1Num  : _hp2Num;
            if (fill == null) return;
            float t = max > 0f ? Mathf.Clamp01(hp / max) : 0f;
            fill.fillAmount = t;
            fill.color = HpGrad.Evaluate(t);
            var fillRect = player == 1 ? _hp1FillRect : _hp2FillRect;
            if (fillRect != null)
            {
                if (player == 1)
                {
                    fillRect.anchorMin = Vector2.zero;
                    fillRect.anchorMax = new Vector2(t, 1f);
                }
                else
                {
                    fillRect.anchorMin = new Vector2(1f - t, 0f);
                    fillRect.anchorMax = Vector2.one;
                }
                fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            }
            if (num) num.text = Mathf.CeilToInt(hp).ToString();
        }

        void UpdateGuard(int player, float guard, float max)
        {
            var fill = player == 1 ? _guard1Fill : _guard2Fill;
            var rect = player == 1 ? _guard1FillRect : _guard2FillRect;
            if (fill == null || rect == null) return;

            float t = max > 0f ? Mathf.Clamp01(guard / max) : 0f;
            if (player == 1)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = new Vector2(t, 1f);
            }
            else
            {
                rect.anchorMin = new Vector2(1f - t, 0f);
                rect.anchorMax = Vector2.one;
            }
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            fill.color = Color.Lerp(new Color(0.85f, 0.15f, 1f), new Color(0.25f, 0.7f, 1f), t);
        }

        // ── HUD構築 ───────────────────────────────────────────────
        void BuildHUD()
        {
            _hudRoot = MakeUI("HUDRoot", transform);
            var rt = _hudRoot.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            BuildTopBar();
            BuildSkillBars();
        }

        // 上部: [1P名前|HPバー  HP数値]  [タイマー]  [HP数値  HPバー|2P名前]
        void BuildTopBar()
        {
            const float barH = 56f;
            const float pad  = 8f;

            // 1P HPバーコンテナ (左半分)
            var c1 = MakeUI("HPContainer1P", _hudRoot.transform);
            Anchor(c1, 0f, 1f, 0.5f, 1f, pad, -barH - pad, -4f, -pad);
            var bg1 = c1.AddComponent<Image>();
            bg1.color = new Color(0f, 0f, 0f, 0.65f);
            AddHPBar(c1.transform, true, out _hp1Fill, out _hp1FillRect, out _guard1Fill, out _guard1FillRect, out _hp1Num, out _hp1Name);

            // 2P HPバーコンテナ (右半分)
            var c2 = MakeUI("HPContainer2P", _hudRoot.transform);
            Anchor(c2, 0.5f, 1f, 1f, 1f, 4f, -barH - pad, -pad, -pad);
            var bg2 = c2.AddComponent<Image>();
            bg2.color = new Color(0f, 0f, 0f, 0.65f);
            AddHPBar(c2.transform, false, out _hp2Fill, out _hp2FillRect, out _guard2Fill, out _guard2FillRect, out _hp2Num, out _hp2Name);

            // タイマー（中央）
            var tc = MakeUI("TimerBox", _hudRoot.transform);
            var tcRt = tc.GetComponent<RectTransform>();
            tcRt.anchorMin = new Vector2(0.5f, 1f);
            tcRt.anchorMax = new Vector2(0.5f, 1f);
            tcRt.sizeDelta = new Vector2(90f, barH);
            tcRt.anchoredPosition = new Vector2(0f, -(barH * 0.5f + pad));
            var tcBg = tc.AddComponent<Image>();
            tcBg.color = new Color(0f, 0f, 0f, 0.75f);

            var timerGo = MakeUI("TimerText", tc.transform);
            FillParent(timerGo);
            _timerText = timerGo.AddComponent<TextMeshProUGUI>();
            _timerText.text      = "60";
            _timerText.fontSize  = 38f;
            _timerText.fontStyle = FontStyles.Bold;
            _timerText.alignment = TextAlignmentOptions.Center;
            _timerText.color     = Color.white;
            UITheme.Apply(_timerText);

            // 上部ボーダーライン
            var line = MakeUI("TopLine", _hudRoot.transform);
            var lRt = line.GetComponent<RectTransform>();
            lRt.anchorMin = new Vector2(0f, 1f);
            lRt.anchorMax = new Vector2(1f, 1f);
            lRt.sizeDelta = new Vector2(0f, 2f);
            lRt.anchoredPosition = new Vector2(0f, -(barH + pad * 2f));
            line.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
        }

        void AddHPBar(Transform parent, bool isP1,
                      out Image fill, out RectTransform fillRect,
                      out Image guardFill, out RectTransform guardFillRect,
                      out TextMeshProUGUI num, out TextMeshProUGUI name)
        {
            // 名前ラベル
            var nameGo = MakeUI("Name", parent);
            var nameRt = nameGo.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(isP1 ? 0f : 1f, 0.5f);
            nameRt.anchorMax = new Vector2(isP1 ? 0f : 1f, 0.5f);
            nameRt.sizeDelta = new Vector2(80f, 40f);
            nameRt.anchoredPosition = new Vector2(isP1 ? 44f : -44f, 0f);
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text      = isP1 ? "1P" : "2P";
            nameTmp.fontSize  = 20f;
            nameTmp.fontStyle = FontStyles.Bold;
            nameTmp.alignment = TextAlignmentOptions.Center;
            nameTmp.color     = isP1 ? P1Color : P2Color;
            UITheme.Apply(nameTmp);
            name = nameTmp;

            // バー背景＋fill
            var barBg = MakeUI("BarBg", parent);
            var bbRt  = barBg.GetComponent<RectTransform>();
            bbRt.anchorMin = new Vector2(0f, 0f);
            bbRt.anchorMax = new Vector2(1f, 1f);
            bbRt.offsetMin = new Vector2(isP1 ? 92f : 8f,  16f);
            bbRt.offsetMax = new Vector2(isP1 ? -54f : -92f, -8f);
            barBg.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.06f, 1f);

            var fillGo = MakeUI("Fill", barBg.transform);
            FillParent(fillGo);
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.type         = Image.Type.Simple;
            fillImg.color        = new Color(0.15f, 0.85f, 0.3f);
            fill = fillImg;
            fillRect = fillGo.GetComponent<RectTransform>();

            var guardBg = MakeUI("GuardBg", parent);
            var gbRt = guardBg.GetComponent<RectTransform>();
            gbRt.anchorMin = new Vector2(0f, 0f);
            gbRt.anchorMax = new Vector2(1f, 0f);
            gbRt.offsetMin = new Vector2(isP1 ? 92f : 8f,  6f);
            gbRt.offsetMax = new Vector2(isP1 ? -54f : -92f, 12f);
            guardBg.AddComponent<Image>().color = new Color(0.02f, 0.02f, 0.04f, 0.95f);

            var guardGo = MakeUI("GuardFill", guardBg.transform);
            FillParent(guardGo);
            guardFill = guardGo.AddComponent<Image>();
            guardFill.type = Image.Type.Simple;
            guardFill.color = new Color(0.25f, 0.7f, 1f);
            guardFillRect = guardGo.GetComponent<RectTransform>();

            // HP数値
            var numGo = MakeUI("HPNum", parent);
            var numRt = numGo.GetComponent<RectTransform>();
            numRt.anchorMin = new Vector2(isP1 ? 1f : 0f, 0.5f);
            numRt.anchorMax = new Vector2(isP1 ? 1f : 0f, 0.5f);
            numRt.sizeDelta = new Vector2(48f, 40f);
            numRt.anchoredPosition = new Vector2(isP1 ? -28f : 28f, 0f);
            var numTmp = numGo.AddComponent<TextMeshProUGUI>();
            numTmp.text      = "100";
            numTmp.fontSize  = 22f;
            numTmp.fontStyle = FontStyles.Bold;
            numTmp.alignment = isP1 ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
            numTmp.color     = Color.white;
            UITheme.Apply(numTmp);
            num = numTmp;

            // プレイヤーカラーのサイドライン
            var sideLine = MakeUI("SideLine", parent);
            var slRt = sideLine.GetComponent<RectTransform>();
            slRt.anchorMin = new Vector2(isP1 ? 0f : 1f, 0f);
            slRt.anchorMax = new Vector2(isP1 ? 0f : 1f, 1f);
            slRt.sizeDelta = new Vector2(4f, 0f);
            slRt.anchoredPosition = Vector2.zero;
            sideLine.AddComponent<Image>().color = isP1 ? P1Color : P2Color;
        }

        // 下部スキルバー
        void BuildSkillBars()
        {
            const float barH = 82f;
            const float pad  = 6f;

            var sb1 = MakeUI("SkillBar1P", _hudRoot.transform);
            Anchor(sb1, 0f, 0f, 0.5f, 0f, pad, pad, -4f, barH + pad);
            BuildSlots(sb1.transform, true);

            var sb2 = MakeUI("SkillBar2P", _hudRoot.transform);
            Anchor(sb2, 0.5f, 0f, 1f, 0f, 4f, pad, -pad, barH + pad);
            BuildSlots(sb2.transform, false);

            // 下部ボーダーライン
            var line = MakeUI("BotLine", _hudRoot.transform);
            var lRt = line.GetComponent<RectTransform>();
            lRt.anchorMin = new Vector2(0f, 0f);
            lRt.anchorMax = new Vector2(1f, 0f);
            lRt.sizeDelta = new Vector2(0f, 2f);
            lRt.anchoredPosition = new Vector2(0f, barH + pad * 2f);
            line.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
        }

        void BuildSlots(Transform parent, bool isP1)
        {
            string[] keys = isP1 ? Keys1P : Keys2P;
            var nameArr = isP1 ? _p1Names : _p2Names;
            var se      = isP1 ? _se1     : _se2;

            for (int i = 0; i < 4; i++)
            {
                float xMin = i * 0.25f + 0.003f;
                float xMax = (i + 1) * 0.25f - 0.003f;

                var slot = MakeUI($"Slot{i}", parent);
                var sRt  = slot.GetComponent<RectTransform>();
                sRt.anchorMin = new Vector2(xMin, 0f);
                sRt.anchorMax = new Vector2(xMax, 1f);
                sRt.offsetMin = sRt.offsetMax = Vector2.zero;

                var bg = slot.AddComponent<Image>();
                bg.color = new Color(0.04f, 0.04f, 0.08f, 0.92f);

                // アクセントカラーボーダー (上辺)
                var topBar = MakeUI("Top", slot.transform);
                var tbRt   = topBar.GetComponent<RectTransform>();
                tbRt.anchorMin = new Vector2(0f, 1f); tbRt.anchorMax = new Vector2(1f, 1f);
                tbRt.sizeDelta = new Vector2(0f, 3f); tbRt.anchoredPosition = Vector2.zero;
                topBar.AddComponent<Image>().color = SlotColor(i);

                // 技名（上段）
                var nm = MakeUI("SkillName", slot.transform);
                var nmRt = nm.GetComponent<RectTransform>();
                nmRt.anchorMin = new Vector2(0f, 0.48f); nmRt.anchorMax = new Vector2(1f, 1f);
                nmRt.offsetMin = new Vector2(5f, 0f); nmRt.offsetMax = new Vector2(-5f, -4f);
                var nmTmp = nm.AddComponent<TextMeshProUGUI>();
                var sk = se?.GetSkill((SkillSlot)i);
                nmTmp.text             = sk?.skill_name ?? SlotJp[i];
                nmTmp.fontSize         = 14f;
                nmTmp.alignment        = TextAlignmentOptions.Center;
                nmTmp.color            = Color.white;
                nmTmp.textWrappingMode = TextWrappingModes.NoWrap;
                nmTmp.overflowMode     = TextOverflowModes.Ellipsis;
                UITheme.Apply(nmTmp);
                nameArr[i] = nmTmp;

                // キー＋スロット種別（下段）
                var kl = MakeUI("Key", slot.transform);
                var klRt = kl.GetComponent<RectTransform>();
                klRt.anchorMin = new Vector2(0f, 0f); klRt.anchorMax = new Vector2(1f, 0.48f);
                klRt.offsetMin = new Vector2(4f, 2f); klRt.offsetMax = new Vector2(-4f, 0f);
                var klTmp = kl.AddComponent<TextMeshProUGUI>();
                klTmp.text      = $"[{keys[i]}]  {SlotJp[i]}";
                klTmp.fontSize  = 11f;
                klTmp.alignment = TextAlignmentOptions.Center;
                klTmp.color     = new Color(0.65f, 0.70f, 0.85f);
                UITheme.Apply(klTmp);
            }
        }

        static Color SlotColor(int i) => i switch
        {
            0 => new Color(1.0f, 0.35f, 0.35f),
            1 => new Color(0.35f, 0.65f, 1.0f),
            2 => new Color(0.35f, 1.0f, 0.55f),
            _ => new Color(1.0f, 0.78f, 0.15f),
        };

        // ── ユーティリティ ────────────────────────────────────────
        static GameObject MakeUI(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return go;
        }

        static void FillParent(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // anchorとoffsetをまとめて設定するヘルパー
        static void Anchor(GameObject go,
            float axMin, float ayMin, float axMax, float ayMax,
            float oxMin, float oyMin, float oxMax, float oyMax)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(axMin, ayMin);
            rt.anchorMax = new Vector2(axMax, ayMax);
            rt.offsetMin = new Vector2(oxMin, oyMin);
            rt.offsetMax = new Vector2(oxMax, oyMax);
        }
    }
}
