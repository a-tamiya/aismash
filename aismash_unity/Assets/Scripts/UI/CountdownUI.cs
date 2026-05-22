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
        static readonly Color Col3      = new Color(0.30f, 0.72f, 1.00f);
        static readonly Color Col2      = new Color(1.00f, 0.80f, 0.12f);
        static readonly Color Col1      = new Color(1.00f, 0.28f, 0.22f);
        static readonly Color ColFight  = new Color(1.00f, 0.86f, 0.08f);

        void Start()
        {
            BuildUI();
            Hide();

            if (BattleManager.Instance != null)
            {
                BattleManager.Instance.OnCountdownChanged += OnCountdown;
                BattleManager.Instance.OnBattleStart      += OnFight;
                BattleManager.Instance.OnBattleEnd        += _ => Hide();
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
            _text.fontStyle = FontStyles.Bold;
            _text.alignment = TextAlignmentOptions.Center;
            _text.color = Color.white;
            UITheme.Apply(_text, 128f, FontStyles.Bold);
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

        void OnFight()
        {
            if (_text == null) return;
            _text.gameObject.SetActive(true);
            _text.text     = "FIGHT!";
            _text.fontSize = 108f;
            _text.color    = ColFight;

            StopAllCoroutines();
            StartCoroutine(Punch(1.4f, 1.0f, 0.20f));
            StartCoroutine(Flash(new Color(ColFight.r, ColFight.g, ColFight.b, 0f), 0.35f, 0.55f));
            StartCoroutine(HideAfter(0.85f));
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
