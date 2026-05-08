using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    public class HPBarUI : MonoBehaviour
    {
        [Header("References")]
        public Fighter fighter;
        public Image fillImage;
        public TextMeshProUGUI nameLabel;
        public TextMeshProUGUI hpLabel;

        static readonly Gradient HpGradient;

        static HPBarUI()
        {
            HpGradient = new Gradient();
            HpGradient.SetKeys(
                new[] {
                    new GradientColorKey(Color.red,    0f),
                    new GradientColorKey(Color.yellow, 0.4f),
                    new GradientColorKey(Color.green,  1f)
                },
                new[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
        }

        void Start()
        {
            if (fighter == null) return;
            fighter.OnHPChanged += Refresh;
            Refresh(fighter.CurrentHP, fighter.maxHP);
        }

        void Refresh(float current, float max)
        {
            float t = max > 0f ? current / max : 0f;
            if (fillImage != null)
            {
                fillImage.fillAmount = t;
                fillImage.color      = HpGradient.Evaluate(t);
            }
            if (hpLabel != null)
                hpLabel.text = Mathf.CeilToInt(current).ToString();
        }
    }
}
