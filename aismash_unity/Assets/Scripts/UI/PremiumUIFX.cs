using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptFighters.UI
{
    // 高品質ボタン共通の、軽い浮き上がりと押し込み。Time.timeScale=0のメニューでも動く。
    public sealed class PremiumButtonMotion : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        RectTransform _rect;
        Button _button;
        Vector3 _baseScale;
        bool _hovered;
        bool _pressed;

        void Awake()
        {
            _rect = transform as RectTransform;
            _button = GetComponent<Button>();
            _baseScale = transform.localScale;
        }

        void OnEnable()
        {
            if (_rect == null) _rect = transform as RectTransform;
            if (_button == null) _button = GetComponent<Button>();
            if (_baseScale == Vector3.zero) _baseScale = Vector3.one;
        }

        void Update()
        {
            if (_rect == null) return;
            bool usable = _button == null || _button.interactable;
            float target = !usable ? 0.985f : _pressed ? 0.965f : _hovered ? 1.035f : 1f;
            Vector3 wanted = _baseScale * target;
            _rect.localScale = Vector3.Lerp(_rect.localScale, wanted,
                1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
        }

        void OnDisable()
        {
            _hovered = _pressed = false;
            if (_rect != null) _rect.localScale = _baseScale;
        }

        public void OnPointerEnter(PointerEventData eventData) => _hovered = true;
        public void OnPointerExit(PointerEventData eventData) { _hovered = false; _pressed = false; }
        public void OnPointerDown(PointerEventData eventData) => _pressed = true;
        public void OnPointerUp(PointerEventData eventData) => _pressed = false;
        public void OnSelect(BaseEventData eventData) => _hovered = true;
        public void OnDeselect(BaseEventData eventData) { _hovered = false; _pressed = false; }
    }

    // 生成背景を静止画のままにせず、視線を邪魔しない2%弱の呼吸モーションを与える。
    public sealed class PremiumBackdropMotion : MonoBehaviour
    {
        RectTransform _rect;
        Vector3 _baseScale;
        float _phase;

        void Awake()
        {
            _rect = transform as RectTransform;
            _baseScale = transform.localScale;
            _phase = Random.value * Mathf.PI * 2f;
        }

        void Update()
        {
            if (_rect == null) return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 0.22f + _phase);
            _rect.localScale = _baseScale * Mathf.Lerp(1.012f, 1.028f, pulse);
        }

        void OnDisable()
        {
            if (_rect != null) _rect.localScale = _baseScale;
        }
    }
}
