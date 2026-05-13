using UnityEngine;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;

namespace PromptFighters.Battle
{
    public enum FighterState { Idle, Moving, Jumping, Falling, Guarding, Stunned, Dead }

    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public class Fighter : MonoBehaviour
    {
        [Header("Stats")]
        public float maxHP = 100f;

        [Header("Movement")]
        public float moveSpeed = 5f;
        public float jumpForce = 12f;

        [Header("Guard")]
        [Range(0f, 1f)] public float guardDamageRatio = 0.15f;

        [Header("Ground Check")]
        public Transform groundCheck;
        public float groundCheckRadius = 0.12f;
        public LayerMask groundLayer;

        // 相手ファイターへの参照（BattleManagerがセット）
        [HideInInspector] public Fighter Opponent;

        public float CurrentHP { get; private set; }
        public FighterState State { get; private set; } = FighterState.Idle;
        public bool IsGrounded { get; private set; }
        public bool FacingRight { get; set; } = true;
        public int PlayerIndex { get; set; }

        public event System.Action<float, float> OnHPChanged;
        public event System.Action               OnDeath;
        public event System.Action<float, bool>  OnDamageReceived; // (damage, wasBlocked)

        Rigidbody2D _rb;
        SpriteRenderer _sprite;
        SpriteRenderer _rootSprite;
        Transform _visualRoot;
        SkillExecutor _skillExecutor;
        float _stunTimer;
        float _controlLockTimer;
        float _skillRecoveryTimer;

        // 状態異常タイマー
        float _burnTimer;
        float _burnTickTimer;
        float _burnDamagePerTick = 2f;
        float _slowTimer;
        float _slowFactor = 0.5f;
        float _guardBreakTimer;
        float _hitFlashTimer;

        static readonly Color GuardColor      = new Color(0.4f, 0.6f, 1f);
        static readonly Color StunColor       = new Color(1f, 0.8f, 0f);
        static readonly Color BurnColor       = new Color(1f, 0.5f, 0.3f);
        static readonly Color SlowColor       = new Color(0.6f, 0.8f, 1f);
        static readonly Color GuardBreakColor = new Color(0.6f, 0.3f, 0.7f);

        public bool CanAct =>
            State != FighterState.Stunned &&
            State != FighterState.Dead    &&
            _controlLockTimer   <= 0f     &&
            _skillRecoveryTimer <= 0f;

        public SpriteRenderer VisualRenderer => _sprite;

        void Awake()
        {
            _rb             = GetComponent<Rigidbody2D>();
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            _rootSprite     = GetComponent<SpriteRenderer>();
            EnsureVisualRenderer();
            _skillExecutor  = GetComponent<SkillExecutor>();
            CurrentHP       = maxHP;
        }

        void Update()
        {
            IsGrounded = groundCheck != null &&
                Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);

            // 自動振り向き: スキル実行中・スタン・死亡以外は常に相手を向く
            AutoFaceOpponent();

            if (_stunTimer          > 0f) _stunTimer          -= Time.deltaTime;
            if (_controlLockTimer   > 0f) _controlLockTimer   -= Time.deltaTime;
            if (_skillRecoveryTimer > 0f) _skillRecoveryTimer -= Time.deltaTime;
            if (_slowTimer          > 0f) _slowTimer          -= Time.deltaTime;
            if (_guardBreakTimer    > 0f) _guardBreakTimer    -= Time.deltaTime;
            if (_hitFlashTimer      > 0f) _hitFlashTimer      -= Time.deltaTime;
            TickBurn();

            UpdateState();
            UpdateVisual();
        }

        void TickBurn()
        {
            if (_burnTimer <= 0f) return;
            _burnTimer     -= Time.deltaTime;
            _burnTickTimer -= Time.deltaTime;
            if (_burnTickTimer <= 0f)
            {
                _burnTickTimer = 0.5f;
                CurrentHP = Mathf.Max(0f, CurrentHP - _burnDamagePerTick);
                OnHPChanged?.Invoke(CurrentHP, maxHP);
                if (CurrentHP <= 0f) Die();
            }
        }

        void UpdateState()
        {
            if (State == FighterState.Dead || State == FighterState.Guarding) return;

            if (_stunTimer > 0f) { State = FighterState.Stunned; return; }

            if (!IsGrounded)
            {
                State = _rb.linearVelocity.y > 0.1f ? FighterState.Jumping : FighterState.Falling;
                return;
            }

            State = Mathf.Abs(_rb.linearVelocity.x) > 0.1f ? FighterState.Moving : FighterState.Idle;
        }

        void UpdateVisual()
        {
            if (_sprite == null) return;

            // ヒットフラッシュ中は白点滅
            if (_hitFlashTimer > 0f)
            {
                _sprite.color = Color.white;
                return;
            }

            Color c = State switch
            {
                FighterState.Guarding => GuardColor,
                FighterState.Stunned  => StunColor,
                _                     => Color.white,
            };

            if (State != FighterState.Guarding && State != FighterState.Stunned)
            {
                if      (_burnTimer       > 0f) c = BurnColor;
                else if (_guardBreakTimer > 0f) c = GuardBreakColor;
                else if (_slowTimer       > 0f) c = SlowColor;
            }

            _sprite.color = c;
        }

        public void Move(float direction)
        {
            if (!CanAct) return;
            float speed = moveSpeed * (_slowTimer > 0f ? _slowFactor : 1f);
            _rb.linearVelocity = new Vector2(direction * speed, _rb.linearVelocity.y);
            if      (direction >  0.1f && !FacingRight) Flip();
            else if (direction < -0.1f &&  FacingRight) Flip();
        }

        public void Jump()
        {
            if (!IsGrounded || !CanAct) return;
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, jumpForce);
        }

        public void SetGuard(bool guarding)
        {
            if (State == FighterState.Dead || _stunTimer > 0f) return;
            if (_guardBreakTimer > 0f) { guarding = false; }

            if (guarding && State != FighterState.Guarding)
            {
                State = FighterState.Guarding;
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            }
            else if (!guarding && State == FighterState.Guarding)
            {
                State = FighterState.Idle;
            }
        }

        public void TakeDamage(float damage, float knockbackForce = 0f,
                               Vector2 knockbackDir = default, float stunDuration = 0f,
                               float guardDamage = 0f)
        {
            if (State == FighterState.Dead) return;

            bool blocking = State == FighterState.Guarding && _guardBreakTimer <= 0f;
            float actual  = blocking ? Mathf.Max(damage * guardDamageRatio, guardDamage) : damage;
            CurrentHP     = Mathf.Max(0f, CurrentHP - actual);
            OnHPChanged?.Invoke(CurrentHP, maxHP);

            if (knockbackForce > 0f && (!blocking || knockbackForce > 6f))
            {
                _rb.linearVelocity = knockbackDir.normalized * knockbackForce;
                _controlLockTimer  = 0.2f;
            }

            if (!blocking && stunDuration > 0f)
                _stunTimer = Mathf.Min(stunDuration, 1.5f);

            OnDamageReceived?.Invoke(actual, blocking);
            DamagePopup.Spawn(transform.position, actual, blocking);
            if (!blocking) _hitFlashTimer = 0.08f;
            if (CurrentHP <= 0f) Die();
        }

        public void ApplyStatus(StatusType type, float duration)
        {
            switch (type)
            {
                case StatusType.Stun:
                    _stunTimer = Mathf.Max(_stunTimer, Mathf.Min(duration, 1.5f));
                    break;
                case StatusType.Burn:
                    _burnTimer     = Mathf.Max(_burnTimer, duration);
                    _burnTickTimer = Mathf.Min(_burnTickTimer, 0.5f);
                    if (_burnTickTimer <= 0f) _burnTickTimer = 0.5f;
                    break;
                case StatusType.Slow:
                    _slowTimer = Mathf.Max(_slowTimer, duration);
                    break;
                case StatusType.GuardBreak:
                    _guardBreakTimer = Mathf.Max(_guardBreakTimer, Mathf.Min(duration, 1.5f));
                    if (State == FighterState.Guarding) State = FighterState.Idle;
                    break;
            }
        }

        public void ApplyImpulse(Vector2 impulse)
        {
            if (State == FighterState.Dead) return;
            _rb.linearVelocity = new Vector2(impulse.x, _rb.linearVelocity.y + impulse.y);
        }

        public void BeginSkillRecovery(float seconds)
        {
            _skillRecoveryTimer = Mathf.Max(_skillRecoveryTimer, seconds);
        }

        public void ResetForBattle(Vector3 spawnPos, bool faceRight = true)
        {
            CurrentHP           = maxHP;
            _stunTimer          = 0f;
            _controlLockTimer   = 0f;
            _skillRecoveryTimer = 0f;
            _burnTimer          = 0f;
            _slowTimer          = 0f;
            _guardBreakTimer    = 0f;
            _hitFlashTimer      = 0f;
            State               = FighterState.Idle;
            transform.position  = spawnPos;
            _rb.linearVelocity  = Vector2.zero;
            FacingRight = faceRight;
            var s = transform.localScale;
            s.x = faceRight ? Mathf.Abs(s.x) : -Mathf.Abs(s.x);
            transform.localScale = s;
            ApplyVisualScaleCorrection();
            OnHPChanged?.Invoke(CurrentHP, maxHP);
        }

        public void SetCharacterSprite(Sprite sprite)
        {
            EnsureVisualRenderer();
            if (_sprite == null) return;
            _sprite.sprite = sprite;
            ApplyVisualScaleCorrection();
        }

        void EnsureVisualRenderer()
        {
            if (_sprite != null) return;

            var visual = transform.Find("Visual");
            if (visual == null)
            {
                var go = new GameObject("Visual");
                go.transform.SetParent(transform, false);
                visual = go.transform;
            }

            _visualRoot = visual;
            _sprite = visual.GetComponent<SpriteRenderer>();
            if (_sprite == null) _sprite = visual.gameObject.AddComponent<SpriteRenderer>();

            if (_rootSprite != null)
            {
                _sprite.sprite = _rootSprite.sprite;
                _sprite.sharedMaterial = _rootSprite.sharedMaterial;
                _sprite.sortingLayerID = _rootSprite.sortingLayerID;
                _sprite.sortingOrder = _rootSprite.sortingOrder;
                _rootSprite.enabled = false;
            }

            ApplyVisualScaleCorrection();
        }

        void ApplyVisualScaleCorrection()
        {
            if (_visualRoot == null) return;

            Vector3 parentScale = transform.localScale;
            float x = Mathf.Abs(parentScale.x);
            float y = Mathf.Abs(parentScale.y);
            if (x <= 0.001f || y <= 0.001f)
            {
                _visualRoot.localScale = Vector3.one;
                return;
            }

            _visualRoot.localPosition = Vector3.zero;
            _visualRoot.localRotation = Quaternion.identity;
            _visualRoot.localScale = new Vector3(y / x, 1f, 1f);
        }

        void Die()
        {
            State = FighterState.Dead;
            _rb.linearVelocity = Vector2.zero;
            OnDeath?.Invoke();
        }

        void AutoFaceOpponent()
        {
            if (Opponent == null) return;
            if (State == FighterState.Dead || State == FighterState.Stunned) return;
            // スキル実行中は方向転換しない（ヒットボックスの位置がずれるため）
            if (_skillExecutor != null && _skillExecutor.IsExecuting) return;

            bool shouldFaceRight = Opponent.transform.position.x > transform.position.x;
            if (shouldFaceRight != FacingRight) Flip();
        }

        void Flip()
        {
            FacingRight = !FacingRight;
            var s = transform.localScale;
            s.x *= -1f;
            transform.localScale = s;
        }

        void OnDrawGizmosSelected()
        {
            if (groundCheck == null) return;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}
