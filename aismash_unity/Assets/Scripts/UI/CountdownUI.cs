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
        Image           _flash;

        // number → (text color, flash color)
        static readonly Color Col3      = UITheme.P1Neon;
        static readonly Color Col2      = UITheme.Gold;
        static readonly Color Col1      = UITheme.Urgent;
        static readonly Color ColFight  = UITheme.Gold;

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

            // text
            var tGo = new GameObject("CDText");
            tGo.layer = gameObject.layer;
            tGo.transform.SetParent(root, false);
            var tRt = tGo.AddComponent<RectTransform>();
            tRt.anchorMin = tRt.anchorMax = new Vector2(0.5f, 0.5f);
            tRt.sizeDelta = new Vector2(600f, 280f);
            tRt.anchoredPosition = new Vector2(0f, 18f);
            _text = tGo.AddComponent<TextMeshProUGUI>();
            _text.fontSize = 128f;
            _text.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _text.alignment = TextAlignmentOptions.Center;
            _text.color = Color.white;
            UITheme.Apply(_text, 128f, FontStyles.Bold | FontStyles.Italic);
        }

        void OnCountdown(float t)
        {
            if (_text == null) return;
            _text.gameObject.SetActive(true);

            int n = Mathf.CeilToInt(t);
            if (n <= 0) return;

            _text.text     = n.ToString();
            _text.fontSize = 128f;
            Color textCol  = n >= 3 ? Col3 : n == 2 ? Col2 : Col1;
            Color flashCol = new Color(textCol.r, textCol.g, textCol.b, 0f);
            _text.color    = textCol;

            StopAllCoroutines();
            StartCoroutine(Punch(1.55f, 1.0f, 0.22f));
            StartCoroutine(Flash(flashCol, 0.28f, 0.45f));
        }

        void OnRoundEnd(int winnerIdx, int p1wins, int p2wins)
        {
            if (_text == null) return;
            _text.gameObject.SetActive(true);
            string label = winnerIdx == 0 ? "1P WIN!" : winnerIdx == 1 ? "2P WIN!" : "DRAW";
            Color col    = winnerIdx == 0 ? UITheme.P1Neon
                         : winnerIdx == 1 ? UITheme.P2Neon
                         :                  UITheme.Gold;
            _text.text     = label;
            _text.fontSize = 96f;
            _text.color    = col;
            StopAllCoroutines();
            StartCoroutine(Punch(1.4f, 1.0f, 0.20f));
            StartCoroutine(Flash(new Color(col.r, col.g, col.b, 0f), 0.30f, 0.40f));
            StartCoroutine(HideAfter(1.8f));
        }

        void OnRoundStart(int round)
        {
            if (_text == null) return;
            _text.gameObject.SetActive(true);
            _text.text     = $"ROUND {round}";
            _text.fontSize = 90f;
            _text.color    = new Color(0.9f, 0.9f, 1f);
            StopAllCoroutines();
            StartCoroutine(Punch(1.5f, 1.0f, 0.22f));
            StartCoroutine(Flash(new Color(0.5f, 0.5f, 1f, 0f), 0.28f, 0.45f));
            StartCoroutine(HideAfter(1.0f));
        }

        void OnFight()
        {
            if (_text == null) return;
            bool coop = BattleManager.Instance != null
                        && BattleManager.Instance.Mode == BattleMode.CoopVsBoss;

            _text.gameObject.SetActive(true);
            _text.text     = coop ? "最強ボスを倒せ！" : "FIGHT!";
            _text.fontSize = coop ? 84f : 108f;
            Color col      = coop ? UITheme.Urgent : ColFight;
            _text.color    = col;

            StopAllCoroutines();
            StartCoroutine(Punch(coop ? 1.7f : 1.4f, 1.0f, coop ? 0.28f : 0.20f));
            StartCoroutine(Flash(new Color(col.r, col.g, col.b, 0f), coop ? 0.45f : 0.35f, coop ? 0.65f : 0.55f));
            StartCoroutine(HideAfter(coop ? 1.2f : 0.85f));
        }

        IEnumerator Punch(float fromScale, float toScale, float duration)
        {
            if (_text == null) yield break;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float s = Mathf.Lerp(fromScale, toScale, t / duration);
                if (_text != null) _text.transform.localScale = Vector3.one * s;
                yield return null;
            }
            if (_text != null) _text.transform.localScale = Vector3.one;
        }

        IEnumerator Flash(Color baseCol, float peakAlpha, float duration)
        {
            if (_flash == null) yield break;
            Color c = baseCol; c.a = peakAlpha;
            _flash.color = c;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                c.a = Mathf.Lerp(peakAlpha, 0f, t / duration);
                if (_flash != null) _flash.color = c;
                yield return null;
            }
            if (_flash != null) _flash.color = new Color(0, 0, 0, 0);
        }

        IEnumerator HideAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            Hide();
        }

        void Hide()
        {
            if (_text  != null) { _text.transform.localScale = Vector3.one; _text.gameObject.SetActive(false); }
            if (_flash != null) _flash.color = new Color(0, 0, 0, 0);
        }
    }
}
