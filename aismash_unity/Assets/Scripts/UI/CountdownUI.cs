using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;

namespace PromptFighters.UI
{
    public class CountdownUI : MonoBehaviour
    {
        TextMeshProUGUI _text;
        Image           _flash;
        Image           _portrait1, _portrait2; // キャラ立ち絵スライドイン用

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

            // キャラ立ち絵（左: 1P、右: 2P）
            _portrait1 = MakePortrait(root, "CDPortrait1", left: true);
            _portrait2 = MakePortrait(root, "CDPortrait2", left: false);

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

            // キャラ立ち絵スライドイン演出
            var bm = BattleManager.Instance;
            if (bm != null)
            {
                SetPortraitSprite(_portrait1, bm.Character1?.spriteSet?.Get(CharacterSpriteId.Idle1), flipX: false);
                SetPortraitSprite(_portrait2, bm.Character2?.spriteSet?.Get(CharacterSpriteId.Idle1), flipX: true);
                StartCoroutine(PortraitSlide(_portrait1, fromLeft: true,  duration: 0.22f, holdTime: 0.70f));
                StartCoroutine(PortraitSlide(_portrait2, fromLeft: false, duration: 0.22f, holdTime: 0.70f));
            }
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
            HidePortrait(_portrait1);
            HidePortrait(_portrait2);
        }

        // ── キャラ立ち絵ヘルパー ────────────────────────────────────────
        static Image MakePortrait(Transform root, string name, bool left)
        {
            var go = new GameObject(name);
            go.layer = root.gameObject.layer;
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(root, false);
            rt.anchorMin = new Vector2(left ? 0f : 1f, 0f);
            rt.anchorMax = new Vector2(left ? 0f : 1f, 1f);
            rt.sizeDelta = new Vector2(280f, 0f);
            rt.anchoredPosition = new Vector2(left ? -320f : 320f, 0f); // 最初は画面外
            var img = go.AddComponent<Image>();
            img.preserveAspect = true;
            img.color = new Color(1f, 1f, 1f, 0f);
            return img;
        }

        static void SetPortraitSprite(Image img, Sprite sprite, bool flipX)
        {
            if (img == null) return;
            img.sprite = sprite;
            img.enabled = sprite != null;
            img.rectTransform.localScale = new Vector3(flipX ? -1f : 1f, 1f, 1f);
        }

        static void HidePortrait(Image img)
        {
            if (img == null) return;
            img.color = new Color(1f, 1f, 1f, 0f);
            img.enabled = false;
        }

        IEnumerator PortraitSlide(Image img, bool fromLeft, float duration, float holdTime)
        {
            if (img == null || img.sprite == null) yield break;
            img.enabled = true;
            img.color   = Color.white;

            float offscreen = fromLeft ? -320f :  320f;
            float onscreen  = fromLeft ?  20f  : -20f;

            var rt = img.rectTransform;

            // スライドイン
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float x = Mathf.Lerp(offscreen, onscreen, Mathf.SmoothStep(0f, 1f, t / duration));
                rt.anchoredPosition = new Vector2(x, 0f);
                yield return null;
            }
            rt.anchoredPosition = new Vector2(onscreen, 0f);

            yield return new WaitForSecondsRealtime(holdTime);

            // フェードアウト
            t = 0f;
            float fadeDur = 0.18f;
            while (t < fadeDur)
            {
                t += Time.unscaledDeltaTime;
                img.color = new Color(1f, 1f, 1f, 1f - t / fadeDur);
                yield return null;
            }
            HidePortrait(img);
        }
    }
}
