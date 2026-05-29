using UnityEngine;
using TMPro;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    public class TimerUI : MonoBehaviour
    {
        public TextMeshProUGUI timerText;
        public Color urgentColor = UITheme.Urgent;
        public float urgentThreshold = 10f;

        Color _normalColor;

        void Start()
        {
            if (timerText != null)
            {
                UITheme.Apply(timerText, 55f, FontStyles.Bold | FontStyles.Italic);
                _normalColor = timerText.color;
            }
            if (BattleManager.Instance != null)
            {
                BattleManager.Instance.OnTimerChanged += Refresh;
                BattleManager.Instance.OnTrainingStart += ShowTraining;
            }
        }

        void Refresh(float seconds)
        {
            if (timerText == null) return;
            int totalSeconds = Mathf.CeilToInt(seconds);
            timerText.text  = $"{totalSeconds / 60}:{totalSeconds % 60:00}";
            timerText.color = seconds <= urgentThreshold ? urgentColor : _normalColor;
        }

        void ShowTraining()
        {
            if (timerText == null) return;
            timerText.text = "トレーニング中";
            timerText.color = new Color(0.5f, 0.85f, 1f);
        }
    }
}
