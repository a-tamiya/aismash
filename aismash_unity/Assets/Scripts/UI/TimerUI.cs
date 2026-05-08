using UnityEngine;
using TMPro;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    public class TimerUI : MonoBehaviour
    {
        public TextMeshProUGUI timerText;
        public Color urgentColor = Color.red;
        public float urgentThreshold = 10f;

        Color _normalColor;

        void Start()
        {
            if (timerText != null) _normalColor = timerText.color;
            if (BattleManager.Instance != null)
                BattleManager.Instance.OnTimerChanged += Refresh;
        }

        void Refresh(float seconds)
        {
            if (timerText == null) return;
            timerText.text  = Mathf.CeilToInt(seconds).ToString();
            timerText.color = seconds <= urgentThreshold ? urgentColor : _normalColor;
        }
    }
}
