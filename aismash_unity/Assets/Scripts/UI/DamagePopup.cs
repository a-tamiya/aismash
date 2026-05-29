using UnityEngine;
using TMPro;

namespace PromptFighters.UI
{
    public class DamagePopup : MonoBehaviour
    {
        // damage level thresholds → size/color
        static readonly Color ColSmall  = new Color(0.95f, 0.95f, 0.95f);
        static readonly Color ColMid    = new Color(1.00f, 0.85f, 0.10f);
        static readonly Color ColBig    = new Color(1.00f, 0.55f, 0.10f);
        static readonly Color ColCrit   = new Color(1.00f, 0.22f, 0.18f);
        static readonly Color ColGuard  = new Color(0.45f, 0.75f, 1.00f);

        TextMeshPro _tmp;
        float       _elapsed;
        float       _riseSpeed;
        const float Duration = 1.0f;

        public static void Spawn(Vector3 worldPos, float damage, bool blocked)
        {
            var go = new GameObject("DmgPopup");
            // slight horizontal randomness
            go.transform.position = worldPos + new Vector3(Random.Range(-0.15f, 0.15f), 1.1f, 0f);

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment    = TextAlignmentOptions.Center;
            tmp.sortingOrder = 20;
            tmp.fontStyle    = FontStyles.Bold | FontStyles.Italic;

            if (blocked)
            {
                tmp.fontSize = 2.8f;
                tmp.color    = ColGuard;
                tmp.text     = $"GUARD  -{Mathf.RoundToInt(damage)}";
            }
            else if (damage >= 30f)
            {
                tmp.fontSize = 5.2f;
                tmp.color    = ColCrit;
                tmp.text     = Mathf.RoundToInt(damage).ToString();
            }
            else if (damage >= 18f)
            {
                tmp.fontSize = 4.4f;
                tmp.color    = ColBig;
                tmp.text     = Mathf.RoundToInt(damage).ToString();
            }
            else if (damage >= 8f)
            {
                tmp.fontSize = 3.6f;
                tmp.color    = ColMid;
                tmp.text     = Mathf.RoundToInt(damage).ToString();
            }
            else
            {
                tmp.fontSize = 2.8f;
                tmp.color    = ColSmall;
                tmp.text     = Mathf.RoundToInt(damage).ToString();
            }

            UITheme.Apply(tmp);

            var popup = go.AddComponent<DamagePopup>();
            popup._riseSpeed = blocked ? 1.0f : Mathf.Lerp(1.2f, 2.2f, Mathf.InverseLerp(0f, 35f, damage));
        }

        public static void SpawnText(Vector3 worldPos, string text, Color color, float fontSize = 3.6f)
        {
            var go = new GameObject("TextPopup");
            go.transform.position = worldPos + Vector3.up * 1.5f;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.fontSize     = fontSize;
            tmp.color        = color;
            tmp.text         = text;
            tmp.fontStyle    = FontStyles.Bold | FontStyles.Italic;
            tmp.alignment    = TextAlignmentOptions.Center;
            tmp.sortingOrder = 21;
            UITheme.Apply(tmp);

            go.AddComponent<DamagePopup>()._riseSpeed = 1.4f;
        }

        void Awake() => _tmp = GetComponent<TextMeshPro>();

        void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _elapsed / Duration;

            // rise + slight decelerate
            transform.position += Vector3.up * (_riseSpeed * (1f - t * 0.6f) * Time.deltaTime);

            // fade out in second half
            if (_tmp != null)
            {
                float alpha = t < 0.45f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.45f) / 0.55f);
                Color c = _tmp.color; c.a = alpha; _tmp.color = c;
            }

            if (_elapsed >= Duration) Destroy(gameObject);
        }
    }
}
