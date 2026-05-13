using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    public class BattleResultUI : MonoBehaviour
    {
        static readonly Color[] WinnerColors =
        {
            new Color(0.2f, 0.6f, 1f),   // 1P — blue
            new Color(1f, 0.35f, 0.2f),  // 2P — red
        };

        GameObject _overlay;
        TextMeshProUGUI _resultText;
        TextMeshProUGUI _subText;
        Image _bg;
        float _animTimer;
        bool _visible;

        static GameObject CreateUIObject(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            return go;
        }

        void Start()
        {
            BuildOverlay();

            if (BattleManager.Instance != null)
            {
                BattleManager.Instance.OnBattleEnd       += ShowResult;
                BattleManager.Instance.OnReturnedToSetup += HidePanel;
            }
        }

        void BuildOverlay()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
            Transform canvasT = canvas != null ? canvas.transform : transform;

            _overlay = CreateUIObject("BattleResultOverlay", canvasT);
            var rt = _overlay.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            // semi-transparent dark background
            _bg = _overlay.AddComponent<Image>();
            _bg.color = new Color(0f, 0f, 0f, 0.72f);

            // vertical stripe accent (center)
            var stripe = CreateUIObject("Stripe", _overlay.transform);
            var srt = stripe.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.5f, 0f);
            srt.anchorMax = new Vector2(0.5f, 1f);
            srt.sizeDelta = new Vector2(4f, 0f);
            srt.anchoredPosition = Vector2.zero;
            stripe.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);

            // result label
            var resultGO = CreateUIObject("ResultText", _overlay.transform);
            var rrt = resultGO.GetComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0f, 0.52f);
            rrt.anchorMax = new Vector2(1f, 0.75f);
            rrt.offsetMin = rrt.offsetMax = Vector2.zero;
            _resultText = resultGO.AddComponent<TextMeshProUGUI>();
            _resultText.text = "";
            _resultText.alignment = TextAlignmentOptions.Center;
            UITheme.Apply(_resultText, 90f, FontStyles.Bold);
            _resultText.color = Color.white;

            // sub label (例: リターンキーでリスタート)
            var subGO = CreateUIObject("SubText", _overlay.transform);
            var subrt = subGO.GetComponent<RectTransform>();
            subrt.anchorMin = new Vector2(0f, 0.38f);
            subrt.anchorMax = new Vector2(1f, 0.52f);
            subrt.offsetMin = subrt.offsetMax = Vector2.zero;
            _subText = subGO.AddComponent<TextMeshProUGUI>();
            _subText.text = "スペースキーでリスタート";
            _subText.alignment = TextAlignmentOptions.Center;
            UITheme.Apply(_subText, 28f);
            _subText.color = new Color(0.75f, 0.75f, 0.75f, 1f);

            _overlay.SetActive(false);
        }

        void ShowResult(int winnerIndex)
        {
            if (_overlay == null) return;
            _overlay.SetActive(true);
            _visible = true;
            _animTimer = 0f;

            Color accent = winnerIndex >= 0 && winnerIndex < WinnerColors.Length
                ? WinnerColors[winnerIndex]
                : new Color(0.9f, 0.85f, 0.3f);

            if (_resultText != null)
            {
                _resultText.text = winnerIndex switch
                {
                    0 => "1P 勝利！",
                    1 => "2P 勝利！",
                    _ => "引き分け"
                };
                _resultText.color = accent;
            }

            if (_bg != null)
                _bg.color = new Color(accent.r * 0.08f, accent.g * 0.08f, accent.b * 0.12f, 0.78f);
        }

        void Update()
        {
            if (!_visible) return;

            _animTimer += Time.deltaTime;
            // subtle pulse on result text
            if (_resultText != null)
            {
                float pulse = 1f + 0.04f * Mathf.Sin(_animTimer * 3f);
                _resultText.transform.localScale = Vector3.one * pulse;
            }

            var kb = Keyboard.current;
            bool newInputPressed = kb != null &&
                (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame);

#if ENABLE_LEGACY_INPUT_MANAGER
            bool legacyInputPressed = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
#else
            bool legacyInputPressed = false;
#endif

            if (newInputPressed || legacyInputPressed)
            {
                HidePanel();
                BattleManager.Instance?.ReturnToSetup();
            }
        }

        void HidePanel()
        {
            _visible = false;
            if (_overlay != null) _overlay.SetActive(false);
        }
    }
}
