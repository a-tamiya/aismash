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
        SpriteRenderer _burst;
        float       _elapsed;
        float       _riseSpeed;
        float       _burstAlpha;
        float       _burstSpin;
        const float Duration = 0.92f;

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
            tmp.outlineColor = new Color32(5, 4, 8, 255);
            tmp.outlineWidth = blocked ? 0.18f : 0.24f;
            tmp.extraPadding = true;

            var popup = go.AddComponent<DamagePopup>();
            popup._riseSpeed = blocked ? 1.0f : Mathf.Lerp(1.2f, 2.2f, Mathf.InverseLerp(0f, 35f, damage));
            popup.CreateBurst(damage, blocked);
            go.transform.localScale = Vector3.one * 0.26f;
            go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-7f, 7f));
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
            tmp.outlineColor = new Color32(5, 4, 8, 255);
            tmp.outlineWidth = 0.2f;
            tmp.extraPadding = true;

            go.AddComponent<DamagePopup>()._riseSpeed = 1.4f;
            go.transform.localScale = Vector3.one * 0.35f;
        }

        void Awake() => _tmp = GetComponent<TextMeshPro>();

        void CreateBurst(float damage, bool blocked)
        {
            Sprite sprite = UITheme.DamageBurst;
            if (sprite == null) return;

            var burstGo = new GameObject("DamageBurst");
            burstGo.transform.SetParent(transform, false);
            burstGo.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            burstGo.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-16f, 16f));
            float size = blocked
                ? 0.64f
                : Mathf.Lerp(0.58f, 1.08f, Mathf.InverseLerp(2f, 36f, damage));
            burstGo.transform.localScale = Vector3.one * size;

            _burst = burstGo.AddComponent<SpriteRenderer>();
            _burst.sprite = sprite;
            _burst.sortingOrder = 19;
            _burst.color = blocked
                ? new Color(0.32f, 0.72f, 1f, 0.72f)
                : new Color(1f, 1f, 1f, damage >= 30f ? 0.98f : 0.82f);
            _burstAlpha = _burst.color.a;
            _burstSpin = Random.Range(-34f, 34f);
        }

        void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _elapsed / Duration;

            // 一瞬大きく飛び出してから着地する、格闘ゲームらしいヒットレスポンス。
            float popT = Mathf.Clamp01(t / 0.24f);
            float c1 = 1.70158f;
            float q = popT - 1f;
            float back = 1f + (c1 + 1f) * q * q * q + c1 * q * q;
            float settle = t <= 0.24f ? Mathf.LerpUnclamped(0.26f, 1.08f, back)
                : Mathf.Lerp(1.08f, 1f, Mathf.Clamp01((t - 0.24f) / 0.24f));
            transform.localScale = Vector3.one * settle;

            // rise + slight decelerate
            transform.position += Vector3.up * (_riseSpeed * (1f - t * 0.6f) * Time.deltaTime);

            // fade out in second half
            if (_tmp != null)
            {
                float alpha = t < 0.45f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.45f) / 0.55f);
                Color c = _tmp.color; c.a = alpha; _tmp.color = c;
            }
            if (_burst != null)
            {
                float burstFade = t < 0.38f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.38f) / 0.62f);
                Color c = _burst.color;
                c.a = _burstAlpha * burstFade;
                _burst.color = c;
                _burst.transform.Rotate(0f, 0f, _burstSpin * Time.deltaTime);
            }

            if (_elapsed >= Duration) Destroy(gameObject);
        }
    }
}
