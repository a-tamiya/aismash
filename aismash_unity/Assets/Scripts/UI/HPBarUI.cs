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
        Image _damageFillImage;
        Image _criticalGlowImage;
        RectTransform _fillRect;
        RectTransform _damageRect;
        float _displayFill = 1f;
        float _damageFill = 1f;
        float _targetFill = 1f;
        float _pulse;
        bool _isRightSide;

        static HPBarUI()
        {
            HpGradient = new Gradient();
            HpGradient.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1f, 0.08f, 0.14f), 0f),
                    new GradientColorKey(new Color(1f, 0.72f, 0.08f), 0.35f),
                    new GradientColorKey(new Color(0.1f, 0.95f, 0.78f), 1f)
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
            BuildVisuals();
            UITheme.Apply(nameLabel, 22f, FontStyles.Bold);
            UITheme.Apply(hpLabel, 26f, FontStyles.Bold);
            fighter.OnHPChanged += Refresh;
            Refresh(fighter.CurrentHP, fighter.maxHP);
        }

        void Update()
        {
            _displayFill = Mathf.MoveTowards(_displayFill, _targetFill, Time.deltaTime * 5.5f);
            _damageFill  = Mathf.MoveTowards(_damageFill,  _targetFill, Time.deltaTime * 1.2f);
            ApplyFill(_displayFill, _damageFill);

            if (_criticalGlowImage != null)
            {
                _pulse += Time.deltaTime * 6f;
                float lowHp = Mathf.InverseLerp(0.35f, 0.0f, _targetFill);
                Color c = _criticalGlowImage.color;
                c.a = lowHp * Mathf.Lerp(0.25f, 0.68f, (Mathf.Sin(_pulse) + 1f) * 0.5f);
                _criticalGlowImage.color = c;
            }
        }

        void Refresh(float current, float max)
        {
            float t = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            _targetFill = t;
            _displayFill = t;
            if (_damageFill < t) _damageFill = t;
            if (fillImage != null)
            {
                fillImage.color = HpGradient.Evaluate(t);
            }
            ApplyFill(_displayFill, _damageFill);
            if (hpLabel != null)
                hpLabel.text = $"{Mathf.CeilToInt(current)}/{Mathf.CeilToInt(max)}";
        }

        void BuildVisuals()
        {
            var root = transform as RectTransform;
            if (root == null) return;

            _isRightSide = root.pivot.x > 0.5f;
            root.sizeDelta = new Vector2(470f, 78f);

            var frame = CreateImage("HP_Frame", transform, 0, new Color(0.01f, 0.014f, 0.026f, 0.90f));
            Stretch(frame.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var topLine = CreateImage("HP_TopLight", transform, 1,
                _isRightSide ? new Color(1f, 0.34f, 0.22f, 0.9f) : new Color(0.25f, 0.85f, 1f, 0.9f));
            Stretch(topLine.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(10f, -5f), new Vector2(-10f, -2f));

            var barBack = CreateImage("HP_BarBack", transform, 2, new Color(0.03f, 0.035f, 0.06f, 0.95f));
            Stretch(barBack.rectTransform, Vector2.zero, Vector2.one, new Vector2(18f, 14f), new Vector2(-18f, -24f));

            _damageFillImage = CreateImage("HP_DamageTrail", transform, 3, new Color(1f, 0.2f, 0.12f, 0.75f));
            _damageRect = _damageFillImage.rectTransform;
            Stretch(_damageRect, Vector2.zero, Vector2.one, new Vector2(18f, 14f), new Vector2(-18f, -24f));

            if (fillImage == null)
                fillImage = CreateImage("HP_Fill", transform, 4, Color.white);
            else
                fillImage.transform.SetSiblingIndex(4);

            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = _isRightSide ? (int)Image.OriginHorizontal.Right : (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 1f;
            fillImage.raycastTarget = false;
            _fillRect = fillImage.rectTransform;
            Stretch(_fillRect, Vector2.zero, Vector2.one, new Vector2(18f, 14f), new Vector2(-18f, -24f));

            _criticalGlowImage = CreateImage("HP_CriticalGlow", transform, 5, new Color(1f, 0f, 0.12f, 0f));
            Stretch(_criticalGlowImage.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 8f), new Vector2(-12f, -18f));

            for (int i = 1; i < 5; i++)
            {
                var tick = CreateImage($"HP_Tick_{i}", transform, 6, new Color(1f, 1f, 1f, 0.16f));
                var rt = tick.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(i / 5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(2f, 32f);
                rt.anchoredPosition = new Vector2(0f, -4f);
            }

            if (nameLabel != null)
            {
                nameLabel.color = _isRightSide ? new Color(1f, 0.68f, 0.56f) : new Color(0.65f, 0.93f, 1f);
                nameLabel.text = _isRightSide ? "PLAYER 2" : "PLAYER 1";
            }

            if (hpLabel != null)
            {
                hpLabel.color = Color.white;
                hpLabel.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }

        void ApplyFill(float mainFill, float trailFill)
        {
            if (fillImage != null) fillImage.fillAmount = mainFill;
            if (_damageFillImage != null)
            {
                _damageFillImage.type = Image.Type.Filled;
                _damageFillImage.fillMethod = Image.FillMethod.Horizontal;
                _damageFillImage.fillOrigin = _isRightSide ? (int)Image.OriginHorizontal.Right : (int)Image.OriginHorizontal.Left;
                _damageFillImage.fillAmount = trailFill;
            }
        }

        static Image CreateImage(string name, Transform parent, int siblingIndex, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetSiblingIndex(Mathf.Min(siblingIndex, parent.childCount - 1));
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }
    }
}
