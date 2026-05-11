using UnityEngine;
using TMPro;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    // BattleManagerのカウントダウンフェーズを表示する。
    // Canvasのどこかにこのコンポーネントをつけるだけで動く。
    public class CountdownUI : MonoBehaviour
    {
        TextMeshProUGUI _text;

        void Start()
        {
            _text = GetComponentInChildren<TextMeshProUGUI>();
            if (_text == null)
            {
                var go = new GameObject("CountdownText");
                go.transform.SetParent(transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(400f, 200f);
                rt.anchoredPosition = Vector2.zero;
                _text = go.AddComponent<TextMeshProUGUI>();
                _text.fontSize   = 80;
                _text.alignment  = TextAlignmentOptions.Center;
                _text.color      = Color.white;
                _text.fontStyle  = FontStyles.Bold;
            }

            Hide();

            if (BattleManager.Instance != null)
            {
                BattleManager.Instance.OnCountdownChanged += OnCountdown;
                BattleManager.Instance.OnBattleStart      += OnFight;
                BattleManager.Instance.OnBattleEnd        += _ => Hide();
            }
        }

        void OnCountdown(float t)
        {
            if (_text != null) _text.gameObject.SetActive(true);
            int n = Mathf.CeilToInt(t);
            _text.text  = n > 0 ? n.ToString() : "FIGHT!";
            _text.color = n > 0 ? Color.white : Color.yellow;
        }

        void OnFight()
        {
            _text.text  = "FIGHT!";
            _text.color = Color.yellow;
            Invoke(nameof(Hide), 0.8f);
        }

        void Hide()
        {
            if (_text != null) _text.gameObject.SetActive(false);
        }
    }
}
