using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    public class CountdownUI : MonoBehaviour
    {
        TextMeshProUGUI _text;
        Image           _numberImage;   // 3/2/1/GO の生成画像（あれば優先表示）
        RectTransform   _display;        // テキスト＋画像の共通アニメ対象
        Image           _flash;

        // number → (text color, flash color)
        static readonly Color Col3      = UITheme.P1Neon;
        static readonly Color Col2      = UITheme.Gold;
        static readonly Color Col1      = UITheme.Urgent;
        static readonly Color ColFight  = UITheme.Gold;

        static Sprite _c3, _c2, _c1, _cgo, _ko; static bool _spritesTried;
        static void EnsureSprites()
        {
            if (_spritesTried) return;
            _spritesTried = true;
            _c3  = Resources.Load<Sprite>("Effects/count_3");
            _c2  = Resources.Load<Sprite>("Effects/count_2");
            _c1  = Resources.Load<Sprite>("Effects/count_1");
            _cgo = Resources.Load<Sprite>("Effects/count_go");
            _ko  = Resources.Load<Sprite>("Effects/ko");
        }

        void Start()
        {
            BuildUI();
            Hide();

            if (BattleManager.Instance != null)
            {
                BattleManager.Instance.OnCountdownChanged += OnCountdown;
                BattleManager.Instance.OnBattleStart      += OnFight;
                BattleManager.Instance.OnBattleEnd        += _ => Hide();
                BattleManager.Instance.OnRoundStart       += OnRoundStart;
                BattleManager.Instance.OnRoundEnd         += OnRoundEnd;
                BattleManager.Instance.OnKnockout         += OnKnockout;
            }
        }

        void BuildUI()
        {
            var canvas = GetComponentInParent<Canvas>() ?? FindAnyObjectByType<Canvas>();
            Transform root = canvas != null ? canvas.transform : transform;

            // full-screen flash layer
            var flashGo = new GameObject("CDFlash");
            flashGo.layer = gameObject.layer;
            flashGo.transform.SetParent(root, false);
            var fRt = flashGo.AddComponent<RectTransform>();
            fRt.anchorMin = Vector2.zero; fRt.anchorMax = Vector2.one;
            fRt.offsetMin = fRt.offsetMax = Vector2.zero;
            _flash = flashGo.AddComponent<Image>();
            _flash.color = new Color(0, 0, 0, 0);
            _flash.raycastTarget = false;

            // 共通表示コンテナ（パンチ演出はこれに適用）
            var dGo = new GameObject("CDDisplay");
            dGo.layer = gameObject.layer;
            dGo.transform.SetParent(root, false);
            _display = dGo.AddComponent<RectTransform>();
            _display.anchorMin = _display.anchorMax = new Vector2(0.5f, 0.5f);
            _display.sizeDelta = new Vector2(720f, 360f);
            _display.anchoredPosition = new Vector2(0f, 18f);

            // 数字/GO の生成画像（中央・アスペクト維持）
            var imgGo = new GameObject("CDImage");
            imgGo.layer = gameObject.layer;
            imgGo.transform.SetParent(_display, false);
            var iRt = imgGo.AddComponent<RectTransform>();
            iRt.anchorMin = Vector2.zero; iRt.anchorMax = Vector2.one;
            iRt.offsetMin = iRt.offsetMax = Vector2.zero;
            _numberImage = imgGo.AddComponent<Image>();
            _numberImage.preserveAspect = true;
            _numberImage.raycastTarget = false;
            _numberImage.enabled = false;

            // テキスト（画像が無い表示＝ROUND/WIN/コープ文言などのフォールバック）
            var tGo = new GameObject("CDText");
            tGo.layer = gameObject.layer;
            tGo.transform.SetParent(_display, false);
            var tRt = tGo.AddComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = tRt.offsetMax = Vector2.zero;
            _text = tGo.AddComponent<TextMeshProUGUI>();
            _text.fontSize = 128f;
            _text.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _text.alignment = TextAlignmentOptions.Center;
            _text.color = Color.white;
            UITheme.Apply(_text, 128f, FontStyles.Bold | FontStyles.Italic);
        }

        // 画像を表示（テキストは隠す）
        void ShowImage(Sprite s)
        {
            _display.gameObject.SetActive(true);
            if (_text != null) _text.enabled = false;
            if (_numberImage != null) { _numberImage.sprite = s; _numberImage.color = Color.white; _numberImage.enabled = true; }
        }

        // テキストを表示（画像は隠す）
        void ShowText(string label, float size, Color col)
        {
            _display.gameObject.SetActive(true);
            if (_numberImage != null) _numberImage.enabled = false;
            if (_text != null) { _text.enabled = true; _text.text = label; _text.fontSize = size; _text.color = col; }
        }

        void OnCountdown(float t)
        {
            if (_display == null) return;
            int n = Mathf.CeilToInt(t);
            if (n <= 0) return;

            EnsureSprites();
            Color textCol  = n >= 3 ? Col3 : n == 2 ? Col2 : Col1;
            Sprite s       = n >= 3 ? _c3 : n == 2 ? _c2 : _c1;
            if (s != null) ShowImage(s);
            else           ShowText(n.ToString(), 128f, textCol);

            Color flashCol = new Color(textCol.r, textCol.g, textCol.b, 0f);
            StopAllCoroutines();
            StartCoroutine(Punch(1.55f, 1.0f, 0.22f));
            StartCoroutine(Flash(flashCol, 0.28f, 0.45f));
        }

        void OnRoundEnd(int winnerIdx, int p1wins, int p2wins)
        {
            if (_display == null) return;
            string label = winnerIdx == 0 ? "1P WIN!" : winnerIdx == 1 ? "2P WIN!" : "DRAW";
            Color col    = winnerIdx == 0 ? UITheme.P1Neon
                         : winnerIdx == 1 ? UITheme.P2Neon
                         :                  UITheme.Gold;
            ShowText(label, 96f, col);
            StopAllCoroutines();
            StartCoroutine(Punch(1.4f, 1.0f, 0.20f));
            StartCoroutine(Flash(new Color(col.r, col.g, col.b, 0f), 0.30f, 0.40f));
            StartCoroutine(HideAfter(1.8f));
        }

        void OnRoundStart(int round)
        {
            if (_display == null) return;
            ShowText($"ROUND {round}", 90f, new Color(0.9f, 0.9f, 1f));
            StopAllCoroutines();
            StartCoroutine(Punch(1.5f, 1.0f, 0.22f));
            StartCoroutine(Flash(new Color(0.5f, 0.5f, 1f, 0f), 0.28f, 0.45f));
            StartCoroutine(HideAfter(1.0f));
        }

        void OnKnockout()
        {
            if (_display == null) return;
            EnsureSprites();
            if (_ko != null) ShowImage(_ko);
            else ShowText("K.O.", 120f, UITheme.Gold);
            StopAllCoroutines();
            StartCoroutine(Punch(1.8f, 1.0f, 0.35f));
            StartCoroutine(Flash(new Color(1f, 0.25f, 0.12f, 0f), 0.5f, 0.7f));
            StartCoroutine(HideAfter(2.4f));
        }

        void OnFight()
        {
            if (_display == null) return;
            bool coop = BattleManager.Instance != null
                        && BattleManager.Instance.Mode == BattleMode.CoopVsBoss;

            EnsureSprites();
            // 通常戦は GO! 画像、協力戦は専用文言（画像なし）。
            if (!coop && _cgo != null) ShowImage(_cgo);
            else ShowText(coop ? "最強ボスを倒せ！" : "FIGHT!", coop ? 84f : 108f, coop ? UITheme.Urgent : ColFight);

            Color col = coop ? UITheme.Urgent : ColFight;
            StopAllCoroutines();
            StartCoroutine(Punch(coop ? 1.7f : 1.4f, 1.0f, coop ? 0.28f : 0.20f));
            StartCoroutine(Flash(new Color(col.r, col.g, col.b, 0f), coop ? 0.45f : 0.35f, coop ? 0.65f : 0.55f));
            StartCoroutine(HideAfter(coop ? 1.2f : 0.85f));
        }

        IEnumerator Punch(float fromScale, float toScale, float duration)
        {
            if (_display == null) yield break;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float s = Mathf.Lerp(fromScale, toScale, t / duration);
                if (_display != null) _display.localScale = Vector3.one * s;
                yield return null;
            }
            if (_display != null) _display.localScale = Vector3.one;
        }

        IEnumerator Flash(Color baseCol, float peakAlpha, float duration)
        {
            if (_flash == null) yield break;
            Color c = baseCol; c.a = peakAlpha;
            _flash.color = c;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                c.a = Mathf.Lerp(peakAlpha, 0f, t / duration);
                if (_flash != null) _flash.color = c;
                yield return null;
            }
            if (_flash != null) _flash.color = new Color(0, 0, 0, 0);
        }

        IEnumerator HideAfter(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            Hide();
        }

        void Hide()
        {
            if (_display != null) { _display.localScale = Vector3.one; _display.gameObject.SetActive(false); }
            if (_flash != null) _flash.color = new Color(0, 0, 0, 0);
        }
    }
}
