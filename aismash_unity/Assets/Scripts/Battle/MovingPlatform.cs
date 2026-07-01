using UnityEngine;

namespace PromptFighters.Battle
{
    // プラットフォームを X 軸方向に正弦波で往復させる。
    // Rigidbody2D.MovePosition を使うことで、乗っているキャラも一緒に動く。
    [RequireComponent(typeof(Rigidbody2D))]
    public class MovingPlatform : MonoBehaviour
    {
        public float originX, originY;
        public float range;
        public float period;
        public float phaseOffset; // 0..1

        Rigidbody2D _rb;

        void Awake() => _rb = GetComponent<Rigidbody2D>();

        void FixedUpdate()
        {
            float angle = (Time.time / period + phaseOffset) * Mathf.PI * 2f;
            _rb.MovePosition(new Vector2(originX + Mathf.Sin(angle) * range, originY));
        }
    }
}
