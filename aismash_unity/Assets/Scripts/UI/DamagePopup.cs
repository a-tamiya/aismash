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
        float       _baseScale;
        const float Duration = 0.70f;

        public static void Spawn(Vector3 worldPos, float damage, bool blocked)
        {
            var go = new GameObject("DmgPopup");
            go.transform.position = worldPos + new Vector3(Random.Range(-0.08f, 0.08f), 0.82f, 0f);

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.alignment    = TextAlignmentOptions.Center;
            tmp.sortingOrder = 20;
            tmp.fontStyle    = FontStyles.Bold | FontStyles.Italic;

            if (blocked)
            {
                tmp.fontSize = 2.1f;
                tmp.color    = ColGuard;
                tmp.text     = $"GUARD  -{Mathf.RoundToInt(damage)}";
            }
            else if (damage >= 30f)
            {
                tmp.fontSize = 3.0f;
                tmp.color    = ColCrit;
                tmp.text     = Mathf.RoundToInt(damage).ToString();
            }
            else if (damage >= 18f)
            {
                tmp.fontSize = 2.7f;
                tmp.color    = ColBig;
                tmp.text     = Mathf.RoundToInt(damage).ToString();
            }
            else if (damage >= 8f)
            {
                tmp.fontSize = 2.4f;
                tmp.color    = ColMid;
                tmp.text     = Mathf.RoundToInt(damage).ToString();
            }
            else
            {
                tmp.fontSize = 2.1f;
                tmp.color    = ColSmall;
                tmp.text     = Mathf.RoundToInt(damage).ToString();
            }

            UITheme.Apply(tmp);
            tmp.outlineColor = new Color32(5, 4, 8, 255);
            tmp.outlineWidth = blocked ? 0.18f : 0.24f;
            tmp.extraPadding = true;

            var popup = go.AddComponent<DamagePopup>();
            popup._riseSpeed = blocked ? 0.55f : Mathf.Lerp(0.65f, 1.0f, Mathf.InverseLerp(0f, 35f, damage));
            popup._baseScale = blocked
                ? 0.48f
                : Mathf.Lerp(0.48f, 0.65f, Mathf.InverseLerp(0f, 35f, damage));
            go.transform.localScale = Vector3.one * (popup._baseScale * 0.78f);
        }

        public static void SpawnText(Vector3 worldPos, string text, Color color, float fontSize = 3.6f)
        {
            var go = new GameObject("TextPopup");
            go.transform.position = worldPos + Vector3.up * 1.15f;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.fontSize     = fontSize;
            tmp.color        = color;
            tmp.text         = text;
            tmp.fontStyle    = FontStyles.Bold | FontStyles.Italic;
            tmp.alignment    = TextAlignmentOptions.Center;
            tmp.sortingOrder = 21;
            UITheme.Apply(tmp);
            tmp.outlineColor = new Color32(5, 4, 8, 255);
            tmp.outlineWidth = 0.2f;
            tmp.extraPadding = true;

            var popup = go.AddComponent<DamagePopup>();
            popup._riseSpeed = 0.75f;
            popup._baseScale = 0.48f;
            go.transform.localScale = Vector3.one * (popup._baseScale * 0.78f);
        }

        void Awake() => _tmp = GetComponent<TextMeshPro>();

        void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _elapsed / Duration;

            // 視界を塞がない、小さく短い数値表示。回転や衝撃画像は使用しない。
            float intro = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.16f));
            transform.localScale = Vector3.one * (_baseScale * Mathf.Lerp(0.78f, 1f, intro));

            transform.position += Vector3.up * (_riseSpeed * (1f - t * 0.45f) * Time.deltaTime);

            if (_tmp != null)
            {
                float alpha = t < 0.48f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.48f) / 0.52f);
                Color c = _tmp.color; c.a = alpha; _tmp.color = c;
            }

            if (_elapsed >= Duration) Destroy(gameObject);
        }
    }
}
