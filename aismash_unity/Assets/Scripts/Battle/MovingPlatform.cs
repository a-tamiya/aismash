using UnityEngine;
using System.Collections.Generic;

namespace PromptFighters.Battle
{
    // プラットフォームを X 軸方向に正弦波で往復させる。
    // Rigidbody2D.MovePosition を使うことで物理的に動かし、
    // OverlapBox で上面の乗客を検出して同じ変位を加算することでキャリー挙動を実現する。
    [RequireComponent(typeof(Rigidbody2D))]
    public class MovingPlatform : MonoBehaviour
    {
        public float originX, originY;
        public float range;
        public float period;
        public float phaseOffset; // 0..1

        Rigidbody2D _rb;
        BoxCollider2D _col;
        Vector2 _prevPos;

        static readonly List<Collider2D> _overlapBuffer = new List<Collider2D>();

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<BoxCollider2D>();
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            _prevPos = new Vector2(originX, originY);

            // 台の移動が摩擦でキャラに水平速度を与えて待機/ダッシュアニメが
            // 高速に切り替わるのを防ぐため摩擦ゼロにする
            var mat = new PhysicsMaterial2D("MovingPlatformNoFriction");
            mat.friction   = 0f;
            mat.bounciness = 0f;
            _col.sharedMaterial = mat;
        }

        void FixedUpdate()
        {
            float angle = (Time.fixedTime / period + phaseOffset) * Mathf.PI * 2f;
            Vector2 newPos = new Vector2(originX + Mathf.Sin(angle) * range, originY);
            Vector2 delta = newPos - _prevPos;

            if (Mathf.Abs(delta.x) > 0.0001f)
                CarryRiders(delta);

            _rb.MovePosition(newPos);
            _prevPos = newPos;
        }

        void CarryRiders(Vector2 delta)
        {
            if (_col == null) return;
            Bounds b = _col.bounds;
            Vector2 checkCenter = new Vector2(b.center.x, b.max.y + 0.1f);
            Vector2 checkSize   = new Vector2(b.size.x * 0.85f, 0.22f);

            var filter = new ContactFilter2D();
            filter.useTriggers = false;

            _overlapBuffer.Clear();
            Physics2D.OverlapBox(checkCenter, checkSize, 0f, filter, _overlapBuffer);
            for (int i = 0; i < _overlapBuffer.Count; i++)
            {
                var rb = _overlapBuffer[i].attachedRigidbody;
                if (rb == null || rb == _rb || rb.bodyType != RigidbodyType2D.Dynamic) continue;
                // 直接位置を加算してプラットフォームに追従させる（小さな変位なのでトンネリング無し）
                rb.position += new Vector2(delta.x, 0f);
            }
        }
    }
}
