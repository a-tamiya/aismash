using UnityEngine;
using TMPro;

namespace PromptFighters.UI
{
    // ヒット時にワールド座標でダメージ数値を浮かせて表示する。
    public class DamagePopup : MonoBehaviour
    {
        static readonly Color BlockColor  = new Color(0.5f, 0.8f, 1f);
        static readonly Color NormalColor = new Color(1f, 0.9f, 0.2f);
        static readonly Color BigColor    = new Color(1f, 0.3f, 0.2f);

        TextMeshPro _tmp;
        float _elapsed;
        const float Duration = 0.9f;

        public static void Spawn(Vector3 worldPos, float damage, bool blocked)
        {
            var go = new GameObject("DmgPopup");
            go.transform.position = worldPos + Vector3.up * 1.2f;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.fontSize  = blocked ? 2.5f : (damage >= 20f ? 4f : 3f);
            tmp.color     = blocked ? BlockColor : (damage >= 20f ? BigColor : NormalColor);
            tmp.text      = blocked ? $"({Mathf.RoundToInt(damage)})" : Mathf.RoundToInt(damage).ToString();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.sortingOrder = 20;
            UITheme.Apply(tmp);

            go.AddComponent<DamagePopup>();
        }

        public static void SpawnText(Vector3 worldPos, string text, Color color, float fontSize = 3.6f)
        {
            var go = new GameObject("TextPopup");
            go.transform.position = worldPos + Vector3.up * 1.5f;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.fontSize  = fontSize;
            tmp.color     = color;
            tmp.text      = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.sortingOrder = 21;
            UITheme.Apply(tmp);

            go.AddComponent<DamagePopup>();
        }

        void Awake() => _tmp = GetComponent<TextMeshPro>();

        void Update()
        {
            _elapsed += Time.deltaTime;
            float t = _elapsed / Duration;

            transform.position += Vector3.up * (1.5f * Time.deltaTime);
            if (_tmp != null)
            {
                Color c = _tmp.color;
                c.a = 1f - t;
                _tmp.color = c;
            }

            if (_elapsed >= Duration) Destroy(gameObject);
        }
    }
}
