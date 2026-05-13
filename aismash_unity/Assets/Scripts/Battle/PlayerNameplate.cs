using TMPro;
using UnityEngine;

namespace PromptFighters.Battle
{
    public class PlayerNameplate : MonoBehaviour
    {
        Transform _target;
        SpriteRenderer _targetRenderer;
        Vector3 _offset;
        TextMeshPro _label;

        public void SetTarget(Transform target, SpriteRenderer targetRenderer, string text, Color color, Vector3 offset)
        {
            _target = target;
            _targetRenderer = targetRenderer;
            _offset = offset;

            if (_label == null)
            {
                _label = gameObject.GetComponent<TextMeshPro>();
                if (_label == null) _label = gameObject.AddComponent<TextMeshPro>();
            }

            _label.text = text;
            _label.fontSize = 5.8f;
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = color;
            _label.outlineWidth = 0.18f;
            _label.outlineColor = new Color(0f, 0f, 0f, 0.9f);
            _label.sortingOrder = 20;
            transform.localScale = Vector3.one * 0.26f;
            ApplyPosition();
        }

        void LateUpdate()
        {
            ApplyPosition();
        }

        void ApplyPosition()
        {
            if (_target == null) return;
            Vector3 position = _target.position + _offset;
            if (_targetRenderer != null)
            {
                Bounds bounds = _targetRenderer.bounds;
                position = new Vector3(bounds.center.x, bounds.max.y + 0.35f, _target.position.z);
            }

            transform.position = position;
            transform.rotation = Quaternion.identity;
        }
    }
}
