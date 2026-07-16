using UnityEngine;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // ボイスボールが生成する、見える範囲と実際の判定が一致した局所フィールド。
    public enum AngelSpatialEffect
    {
        DirectionalWind,
        RadialWind,
        Gravity,
        Lava,
        Heal,
        Damage,
    }

    public class AngelSpatialZone : MonoBehaviour
    {
        AngelSpatialEffect _effect;
        Vector2 _direction;
        float _strength;
        float _warningRemaining;
        float _durationRemaining;
        float _tickTimer;
        bool _circle;
        Fighter[] _targets;
        SpriteRenderer _renderer;
        Color _activeColor;

        public void Init(AngelSpatialEffect effect, Vector2 worldSize, bool circle,
            Vector2 direction, float strength, float duration, float warningSeconds,
            Fighter[] targets)
        {
            _effect = effect;
            _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            _strength = strength;
            _warningRemaining = Mathf.Max(0f, warningSeconds);
            _durationRemaining = Mathf.Max(0.1f, duration);
            _circle = circle;
            _targets = targets ?? new Fighter[0];

            // 帯状の風・重力は向きに沿わせる。判定はtransform.InverseTransformPointを
            // 使うため、回転後も表示と同じ長方形になる。
            if (!circle && effect != AngelSpatialEffect.Lava && worldSize.x > worldSize.y)
                transform.rotation = Quaternion.Euler(0f, 0f,
                    Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg);

            var sprite = circle ? RuntimeSprite.Circle() : RuntimeSprite.Square();
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = sprite;
            _renderer.sortingOrder = 4;
            _activeColor = EffectColor(effect);
            _renderer.color = WarningColor(_activeColor, 0.32f);

            Vector2 spriteSize = sprite != null ? sprite.bounds.size : Vector2.one;
            transform.localScale = new Vector3(
                worldSize.x / Mathf.Max(0.01f, spriteSize.x),
                worldSize.y / Mathf.Max(0.01f, spriteSize.y), 1f);

            if (effect == AngelSpatialEffect.DirectionalWind ||
                effect == AngelSpatialEffect.RadialWind || effect == AngelSpatialEffect.Gravity)
            {
                AddDirectionIndicator(worldSize);
            }
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (_warningRemaining > 0f)
            {
                _warningRemaining -= dt;
                if (_renderer != null)
                {
                    float pulse = 0.22f + 0.18f * (Mathf.Sin(Time.time * 18f) * 0.5f + 0.5f);
                    _renderer.color = WarningColor(_activeColor, pulse);
                }
                return;
            }

            _durationRemaining -= dt;
            if (_durationRemaining <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            if (_renderer != null)
            {
                float pulse = 0.14f + 0.08f * (Mathf.Sin(Time.time * 5f) * 0.5f + 0.5f);
                _renderer.color = new Color(_activeColor.r, _activeColor.g, _activeColor.b, pulse);
            }

            bool tickNow = false;
            if (_effect == AngelSpatialEffect.Lava || _effect == AngelSpatialEffect.Heal ||
                _effect == AngelSpatialEffect.Damage)
            {
                _tickTimer += dt;
                if (_tickTimer >= 0.5f)
                {
                    _tickTimer -= 0.5f;
                    tickNow = true;
                }
            }

            for (int i = 0; i < _targets.Length; i++)
            {
                Fighter fighter = _targets[i];
                if (fighter == null || fighter.State == FighterState.Dead || fighter.IsDowned ||
                    !OverlapsFighter(fighter)) continue;

                switch (_effect)
                {
                    case AngelSpatialEffect.DirectionalWind:
                        fighter.AddExternalForce(_direction * _strength);
                        break;
                    case AngelSpatialEffect.RadialWind:
                        Vector2 radial = (Vector2)fighter.transform.position - (Vector2)transform.position;
                        if (radial.sqrMagnitude > 0.01f)
                            fighter.AddExternalForce(radial.normalized * _strength);
                        break;
                    case AngelSpatialEffect.Gravity:
                        fighter.AddExternalForce(_direction * _strength);
                        break;
                    case AngelSpatialEffect.Lava:
                        if (tickNow && fighter.IsGrounded)
                            fighter.DrainHP(Mathf.Clamp(_strength * 0.5f, 0.005f, 0.05f));
                        break;
                    case AngelSpatialEffect.Heal:
                        if (tickNow)
                            fighter.HealHP(fighter.MaxHP * Mathf.Clamp(_strength * 0.5f, 0.005f, 0.05f));
                        break;
                    case AngelSpatialEffect.Damage:
                        if (tickNow)
                            fighter.DrainHP(Mathf.Clamp(_strength * 0.5f, 0.005f, 0.04f));
                        break;
                }
            }
        }

        bool Contains(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            if (_circle)
                return local.x * local.x + local.y * local.y <= 0.25f;
            return Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f;
        }

        // キャラクターの足元1点だけではなく、実際のbody colliderと表示領域の重なりで判定する。
        // 領域中心が体内にある場合と、体の中心・最近傍点・外周が領域内にある場合の両方を拾う。
        bool OverlapsFighter(Fighter fighter)
        {
            if (fighter == null) return false;
            var body = fighter.GetComponent<Collider2D>();
            if (body == null) return Contains(fighter.transform.position);
            if (body.OverlapPoint(transform.position)) return true;

            Bounds b = body.bounds;
            Vector2 closest = body.ClosestPoint(transform.position);
            Vector2[] samples =
            {
                b.center,
                closest,
                new Vector2(b.min.x, b.min.y),
                new Vector2(b.min.x, b.max.y),
                new Vector2(b.max.x, b.min.y),
                new Vector2(b.max.x, b.max.y),
                new Vector2(b.center.x, b.min.y),
                new Vector2(b.center.x, b.max.y),
                new Vector2(b.min.x, b.center.y),
                new Vector2(b.max.x, b.center.y),
            };
            for (int i = 0; i < samples.Length; i++)
                if (Contains(samples[i])) return true;
            return false;
        }

        void AddDirectionIndicator(Vector2 worldSize)
        {
            var arrow = new GameObject("Direction");
            arrow.transform.SetParent(transform, false);
            arrow.transform.localPosition = Vector3.zero;
            float parentAngle = transform.eulerAngles.z;
            float worldAngle = Vector2.SignedAngle(Vector2.down, _direction);
            arrow.transform.localRotation = Quaternion.Euler(0f, 0f, worldAngle - parentAngle);
            arrow.transform.localScale = new Vector3(
                0.55f / Mathf.Max(worldSize.x, 0.1f),
                0.8f / Mathf.Max(worldSize.y, 0.1f), 1f);
            var sr = arrow.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprite.DownArrow();
            sr.color = new Color(1f, 1f, 1f, 0.75f);
            sr.sortingOrder = 5;
        }

        static Color EffectColor(AngelSpatialEffect effect)
        {
            switch (effect)
            {
                case AngelSpatialEffect.DirectionalWind: return new Color(0.25f, 0.95f, 1f);
                case AngelSpatialEffect.RadialWind:      return new Color(0.2f, 1f, 0.72f);
                case AngelSpatialEffect.Gravity:         return new Color(0.62f, 0.28f, 1f);
                case AngelSpatialEffect.Lava:            return new Color(1f, 0.22f, 0.04f);
                case AngelSpatialEffect.Heal:            return new Color(0.2f, 1f, 0.42f);
                default:                                 return new Color(1f, 0.12f, 0.35f);
            }
        }

        static Color WarningColor(Color source, float alpha)
            => new Color(Mathf.Max(source.r, 1f), source.g * 0.55f, source.b * 0.35f, alpha);
    }

    // 直線上を進む落下物などの、発生前だけ表示する危険範囲。
    public class AngelTelegraph : MonoBehaviour
    {
        SpriteRenderer _renderer;
        Color _color;
        float _remaining;

        public void Init(Vector2 worldSize, Color color, float duration)
        {
            _color = color;
            _remaining = Mathf.Max(0.1f, duration);
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = RuntimeSprite.Square();
            _renderer.color = color;
            _renderer.sortingOrder = 9;
            transform.localScale = new Vector3(worldSize.x, worldSize.y, 1f);
        }

        void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            if (_renderer != null)
            {
                float a = 0.22f + 0.30f * (Mathf.Sin(Time.time * 20f) * 0.5f + 0.5f);
                _renderer.color = new Color(_color.r, _color.g, _color.b, a);
            }
        }
    }

    public class AngelTimedDestroy : MonoBehaviour
    {
        public void Init(float lifetime) => Destroy(gameObject, Mathf.Clamp(lifetime, 0.5f, 20f));
    }
}
