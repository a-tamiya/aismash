using UnityEngine;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;

namespace PromptFighters.Battle
{
    public enum FighterState { Idle, Moving, Jumping, Falling, Guarding, Dodging, Stunned, Grabbed, Dead }

    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public class Fighter : MonoBehaviour
    {
        [Header("Stats")]
        public float maxHP = 300f;

        [Header("Movement")]
        public float moveSpeed = 5f;
        public float airMoveSpeed = 4f;
        public float jumpForce = 12f;
        public int maxAirJumps = 1;
        [Range(0.45f, 0.85f)] public float shortHopMultiplier = 0.62f;
        public float fastFallSpeed = 7.5f;
        public float fastFallMinAirTime = 0.18f;
        public float fastFallMaxUpwardSpeed = 1.2f;
        [Range(0.2f, 0.5f)] public float walkSpeedRatio = 0.35f;
        [Range(0.3f, 0.6f)] public float airJumpHeightMultiplier = 0.45f;
        [Range(0.5f, 0.95f)] public float dashInputThreshold = 0.75f;
        [Range(0.6f, 1.6f)] public float weight = 1f;

        [Header("Guard")]
        [Range(0f, 1f)] public float guardDamageRatio = 0f;
        public float maxGuardDurability = 65f;
        public float guardTimeDrainPerSecond = 18f;
        public float guardHitDamageRatio = 0.25f;
        public float guardRecoveryPerSecond = 8f;
        public float guardRecoveryDelay = 0f;
        public float guardBreakLockDuration = 5f;

        [Header("Dodge")]
        public float groundDodgeDistance = 2.2f;
        public float airDodgeDistance = 1.8f;
        public float dodgeDuration = 0.28f;
        public float downDodgeDuration = 0.24f;
        public float dodgeFallDistance = 1.7f;

        [Header("Grab / Throw")]
        public GrabParameters grabParameters = new GrabParameters();
        public ThrowParameters throwParameters = new ThrowParameters();
        public float maxGrabHoldSeconds = 1.1f;
        public float grabReleaseRecovery = 0.12f;
        public float throwGrabCooldown = 0.45f;
        [Range(0.1f, 2f)] public float throwKnockbackScale = 1.1f;
        public float throwReleaseOffset = 1.25f;

        [Header("Ground Check")]
        public Transform groundCheck;
        public float groundCheckRadius = 0.12f;
        public LayerMask groundLayer;

        // 相手ファイターへの参照（BattleManagerがセット）
        Fighter _opponentBacking;
        [HideInInspector] public Fighter Opponent
        {
            get => _opponentBacking;
            set
            {
                _opponentBacking = value;
                if (_bodyCollider != null && _opponentBacking?._bodyCollider != null)
                    Physics2D.IgnoreCollision(_bodyCollider, _opponentBacking._bodyCollider, true);
            }
        }

        public float CurrentHP { get; private set; }
        public float CurrentGuardDurability { get; private set; }
        public FighterState State { get; private set; } = FighterState.Idle;
        public bool IsGrounded { get; private set; }
        public bool FacingRight { get; set; } = true;
        public int PlayerIndex { get; set; }
        public bool IsHoldingOpponent => _heldOpponent != null;
        public bool IsGrabbed => _grabbedBy != null;
        public bool IsDodging => State == FighterState.Dodging || _dodgeTimer > 0f;
        public bool IsSkillLocked => _skillRecoveryTimer > 0f || (_skillExecutor != null && _skillExecutor.IsExecuting);

        public event System.Action<float, float> OnHPChanged;
        public event System.Action<float, float> OnGuardChanged;
        public event System.Action               OnGuardBroken;
        public event System.Action               OnDeath;
        public event System.Action<float, bool>  OnDamageReceived; // (damage, wasBlocked)
        public event System.Action               OnJumped;
        public event System.Action               OnLanded;
        public event System.Action               OnDodged;

        Rigidbody2D _rb;
        SpriteRenderer _sprite;
        SpriteRenderer _rootSprite;
        Transform _visualRoot;
        SkillExecutor _skillExecutor;
        CharacterSpriteSet _spriteSet = new CharacterSpriteSet();
        CharacterSpriteId? _forcedSprite;
        float _forcedSpriteTimer;
        float _idleAnimTimer;
        int _idleFrame;
        float _stunTimer;
        float _controlLockTimer;
        float _skillRecoveryTimer;
        float _smashChargeVisualTimer;
        float _smashChargeVisual01;
        float _dodgeTimer;
        float _defaultGravityScale;
        bool _airDodgeUsed;
        bool _isAirDodgeActive;
        bool _dodgeGravitySuppressed;
        bool _fastFallUsed;
        float _airTime;
        int _airJumpsRemaining;
        Collider2D _bodyCollider;
        Collider2D _ignoredOpponentCollider;

        // 状態異常タイマー
        float _burnTimer;
        float _burnTickTimer;
        float _burnDamagePerTick = 2f;
        float _slowTimer;
        float _slowFactor = 0.5f;
        float _guardBreakTimer;
        float _guardRecoveryDelayTimer;
        float _hitFlashTimer;
        Fighter _heldOpponent;
        Fighter _grabbedBy;
        float _grabHoldTimer;
        float _grabCooldownTimer;
        bool _isTryingGrab;

        static readonly Color GuardColor      = new Color(0.4f, 0.6f, 1f);
        static readonly Color StunColor       = new Color(1f, 0.8f, 0f);
        static readonly Color BurnColor       = new Color(1f, 0.5f, 0.3f);
        static readonly Color SlowColor       = new Color(0.6f, 0.8f, 1f);
        static readonly Color GuardBreakColor = new Color(0.6f, 0.3f, 0.7f);

        // ====== ギミック用プロパティ ======
        public float DamageMultiplier { get; private set; } = 1f;
        public bool  IsInvincible     { get; private set; }
        public bool  InputReversed    { get; private set; }

        public bool CanAct =>
            State != FighterState.Guarding &&
            State != FighterState.Dodging &&
            State != FighterState.Stunned &&
            State != FighterState.Grabbed &&
            State != FighterState.Dead    &&
            !_isTryingGrab                &&
            _heldOpponent == null         &&
            _grabbedBy == null            &&
            _guardBreakTimer    <= 0f     &&
            _controlLockTimer   <= 0f     &&
            _skillRecoveryTimer <= 0f;

        public SpriteRenderer VisualRenderer => _sprite;

        void Awake()
        {
            _rb             = GetComponent<Rigidbody2D>();
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            _defaultGravityScale = _rb.gravityScale;
            maxHP           = Mathf.Max(maxHP, 300f);
            maxGrabHoldSeconds = Mathf.Clamp(maxGrabHoldSeconds, 0.45f, 1.1f);
            grabReleaseRecovery = Mathf.Clamp(grabReleaseRecovery, 0.08f, 0.16f);
            throwGrabCooldown = Mathf.Clamp(throwGrabCooldown, 0.25f, 0.6f);
            throwKnockbackScale = Mathf.Max(throwKnockbackScale, 1.1f);
            guardRecoveryDelay = 0f;
            guardRecoveryPerSecond = 8f;
            guardHitDamageRatio = Mathf.Min(guardHitDamageRatio, 0.25f);
            _rootSprite     = GetComponent<SpriteRenderer>();
            _bodyCollider   = GetComponent<Collider2D>();
            EnsureVisualRenderer();
            ApplyColliderScaleCorrection();
            _skillExecutor  = GetComponent<SkillExecutor>();
            CurrentHP       = maxHP;
            CurrentGuardDurability = maxGuardDurability;
        }

        void Update()
        {
            bool wasGrounded = IsGrounded;
            IsGrounded = groundCheck != null &&
                Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
            if (!wasGrounded && IsGrounded && BattleManager.Instance != null && BattleManager.Instance.IsFighting)
                OnLanded?.Invoke();
            if (IsGrounded && _isAirDodgeActive && State == FighterState.Dodging)
            {
                EndAirDodgeOnLanding();
            }
            if (IsGrounded && State != FighterState.Dodging)
            {
                _airDodgeUsed = false;
                _fastFallUsed = false;
                _airTime = 0f;
                _airJumpsRemaining = maxAirJumps;
            }
            else if (!IsGrounded)
            {
                _airTime += Time.deltaTime;
            }

            if (_stunTimer          > 0f) _stunTimer          -= Time.deltaTime;
            if (_controlLockTimer   > 0f) _controlLockTimer   -= Time.deltaTime;
            if (_skillRecoveryTimer > 0f) _skillRecoveryTimer -= Time.deltaTime;
            if (_smashChargeVisualTimer > 0f) _smashChargeVisualTimer -= Time.deltaTime;
            if (_dodgeTimer > 0f) _dodgeTimer -= Time.deltaTime;
            if (_grabCooldownTimer  > 0f) _grabCooldownTimer  -= Time.deltaTime;
            if (_slowTimer          > 0f) _slowTimer          -= Time.deltaTime;
            if (_guardRecoveryDelayTimer > 0f) _guardRecoveryDelayTimer -= Time.deltaTime;
            if (_guardBreakTimer    > 0f)
            {
                _guardBreakTimer -= Time.deltaTime;
                if (_guardBreakTimer <= 0f) EndGuardBreak();
            }
            if (_hitFlashTimer      > 0f) _hitFlashTimer      -= Time.deltaTime;
            if (_forcedSpriteTimer  > 0f)
            {
                _forcedSpriteTimer -= Time.deltaTime;
                if (_forcedSpriteTimer <= 0f) _forcedSprite = null;
            }
            TickBurn();
            TickGuard();
            TickGrab();
            TickDodge();

            UpdateState();
            UpdateVisual();
        }

        void LateUpdate()
        {
            ClampToStage();
            SeparateFromOpponent();
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
            if (State == FighterState.Dead || State == FighterState.Guarding || State == FighterState.Dodging || State == FighterState.Grabbed) return;

            if (_stunTimer > 0f || _guardBreakTimer > 0f) { State = FighterState.Stunned; return; }
            if (_grabbedBy != null) { State = FighterState.Grabbed; return; }

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
            UpdateStateSprite();

            // ヒットフラッシュ中は白点滅
            if (_hitFlashTimer > 0f)
            {
                _sprite.color = Color.white;
                return;
            }

            Color c = State switch
            {
                FighterState.Guarding => GuardColor,
                FighterState.Dodging  => new Color(GuardColor.r, GuardColor.g, GuardColor.b, 0.48f),
                FighterState.Stunned  => StunColor,
                FighterState.Grabbed  => StunColor,
                _                     => Color.white,
            };

            if (_guardBreakTimer > 0f)
            {
                float pulse = (Mathf.Sin(Time.time * 18f) + 1f) * 0.5f;
                _sprite.color = Color.Lerp(GuardBreakColor, Color.white, pulse * 0.35f);
                return;
            }

            if (_smashChargeVisualTimer > 0f)
            {
                float pulse = (Mathf.Sin(Time.time * 28f) + 1f) * 0.5f;
                Color chargeColor = Color.Lerp(new Color(1f, 0.85f, 0.15f), new Color(1f, 0.35f, 0.05f), _smashChargeVisual01);
                _sprite.color = Color.Lerp(chargeColor, Color.white, pulse * 0.25f);
                return;
            }

            if (State != FighterState.Guarding && State != FighterState.Dodging && State != FighterState.Stunned)
            {
                if      (_burnTimer       > 0f) c = BurnColor;
                else if (_slowTimer       > 0f) c = SlowColor;
            }

            _sprite.color = c;
        }

        void UpdateStateSprite()
        {
            Sprite next = null;
            if (_forcedSprite.HasValue)
            {
                next = _spriteSet.Get(_forcedSprite.Value, _sprite.sprite);
            }
            else
            {
                CharacterSpriteId id = State switch
                {
                    FighterState.Moving => CharacterSpriteId.Dash,
                    FighterState.Jumping => CharacterSpriteId.Jump,
                    FighterState.Falling => CharacterSpriteId.Jump,
                    FighterState.Stunned => CharacterSpriteId.Damage,
                    FighterState.Grabbed => CharacterSpriteId.Damage,
                    _ => CurrentIdleSpriteId(),
                };
                next = _spriteSet.Get(id, _sprite.sprite);
            }

            if (next != null && _sprite.sprite != next)
                _sprite.sprite = next;
        }

        CharacterSpriteId CurrentIdleSpriteId()
        {
            _idleAnimTimer += Time.deltaTime;
            if (_idleAnimTimer >= 0.3f)
            {
                _idleAnimTimer = 0f;
                _idleFrame = (_idleFrame + 1) % 3;
            }

            return _idleFrame switch
            {
                1 => CharacterSpriteId.Idle2,
                2 => CharacterSpriteId.Idle3,
                _ => CharacterSpriteId.Idle1,
            };
        }

        public void Move(float direction)
        {
            if (!CanAct && !(CanAirDriftDuringSkill() && Mathf.Abs(direction) > 0.01f)) return;
            float input = Mathf.Clamp(direction, -1f, 1f);
            float absInput = Mathf.Abs(input);
            float baseSpeed = IsGrounded ? moveSpeed : airMoveSpeed;
            float modeScale = absInput > 0.01f && absInput < dashInputThreshold
                ? walkSpeedRatio
                : 1f;
            float speed = baseSpeed * modeScale * (_slowTimer > 0f ? _slowFactor : 1f);
            float velocityX = absInput > 0.01f ? Mathf.Sign(input) * speed : 0f;
            _rb.linearVelocity = new Vector2(velocityX, _rb.linearVelocity.y);
            if      (input >  0.1f && !FacingRight) Flip();
            else if (input < -0.1f &&  FacingRight) Flip();
        }

        bool CanAirDriftDuringSkill()
        {
            return !IsGrounded &&
                   IsSkillLocked &&
                   State != FighterState.Dodging &&
                   State != FighterState.Stunned &&
                   State != FighterState.Grabbed &&
                   State != FighterState.Dead &&
                   _heldOpponent == null &&
                   _grabbedBy == null &&
                   _guardBreakTimer <= 0f &&
                   _controlLockTimer <= 0f;
        }

        public void Jump(float forceMultiplier = 1f)
        {
            if (!CanAct) return;
            float force = jumpForce * Mathf.Clamp(forceMultiplier, 0.45f, 1f);
            if (!IsGrounded)
            {
                if (_airJumpsRemaining <= 0) return;
                _airJumpsRemaining--;
                force = jumpForce * Mathf.Sqrt(Mathf.Clamp(airJumpHeightMultiplier, 0.3f, 0.6f));
            }
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, force);
            _airTime = 0f;
            _fastFallUsed = false;
            OnJumped?.Invoke();
        }

        public void FastFall()
        {
            if (State == FighterState.Dead || State == FighterState.Stunned || State == FighterState.Grabbed) return;
            if (IsGrounded || _dodgeTimer > 0f || _dodgeGravitySuppressed) return;
            if (_fastFallUsed || _airTime < fastFallMinAirTime) return;
            if (_rb.linearVelocity.y > fastFallMaxUpwardSpeed) return;
            float targetY = -Mathf.Abs(fastFallSpeed);
            if (_rb.linearVelocity.y > targetY)
            {
                _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, targetY);
                _fastFallUsed = true;
            }
        }

        public void SetGuard(bool guarding)
        {
            if (State == FighterState.Dead || _stunTimer > 0f) return;
            if (guarding && !IsGrounded) guarding = false;
            if (_skillExecutor != null && _skillExecutor.IsExecuting) return;
            if (_heldOpponent != null || _grabbedBy != null || _isTryingGrab || _dodgeTimer > 0f) return;
            if (_guardBreakTimer > 0f) { guarding = false; }
            if (CurrentGuardDurability <= 0f) { guarding = false; }

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
            if (State == FighterState.Dodging || _dodgeTimer > 0f) return;
            if (_grabbedBy != null) return;
            if (IsInvincible) return;

            bool blocking = State == FighterState.Guarding && _guardBreakTimer <= 0f;
            if (!blocking && Opponent != null) damage *= Opponent.DamageMultiplier;
            float actual  = blocking ? Mathf.Max(0f, damage * guardDamageRatio) : damage;
            CurrentHP     = Mathf.Max(0f, CurrentHP - actual);
            OnHPChanged?.Invoke(CurrentHP, maxHP);
            if (blocking)
                DamageGuard(Mathf.Max(guardDamage, damage * guardHitDamageRatio));
            else if (_guardBreakTimer > 0f)
                EndGuardBreak();

            if (knockbackForce > 0f && (!blocking || knockbackForce > 6f))
            {
                float weightScale = 1f / Mathf.Max(0.4f, weight);
                _rb.linearVelocity = knockbackDir.normalized * knockbackForce * weightScale;
                _controlLockTimer  = 0.2f;
            }

            if (!blocking && stunDuration > 0f)
                _stunTimer = Mathf.Min(stunDuration, 1.5f);

            OnDamageReceived?.Invoke(actual, blocking);
            if (!blocking && Opponent != null)
                BattleLogger.Instance?.LogDamage(Opponent.PlayerIndex, actual);
            if (blocking) DamagePopup.SpawnText(transform.position, "GUARD", GuardColor, 1.6f);
            else          DamagePopup.Spawn(transform.position, actual, false);
            if (!blocking) _hitFlashTimer = 0.08f;
            if (CurrentHP <= 0f) Die();
        }

        public void ApplyStatus(StatusType type, float duration)
        {
            switch (type)
            {
                case StatusType.Stun:
                    _stunTimer = Mathf.Max(_stunTimer, Mathf.Min(duration, 1.5f));
                    DamagePopup.SpawnText(transform.position, "STUN", StunColor, 1.8f);
                    break;
                case StatusType.Burn:
                    _burnTimer     = Mathf.Max(_burnTimer, duration);
                    _burnTickTimer = Mathf.Min(_burnTickTimer, 0.5f);
                    if (_burnTickTimer <= 0f) _burnTickTimer = 0.5f;
                    DamagePopup.SpawnText(transform.position, "BURN", BurnColor, 1.8f);
                    break;
                case StatusType.Slow:
                    _slowTimer = Mathf.Max(_slowTimer, duration);
                    DamagePopup.SpawnText(transform.position, "SLOW", SlowColor, 1.8f);
                    break;
                case StatusType.GuardBreak:
                    BreakGuard(Mathf.Max(1.5f, duration));
                    break;
            }
        }

        public void ApplyImpulse(Vector2 impulse)
        {
            if (State == FighterState.Dead) return;
            _rb.linearVelocity = new Vector2(impulse.x, _rb.linearVelocity.y + impulse.y);
        }

        public void ShowSkillSprite(SkillSlot slot, float seconds)
        {
            CharacterSpriteId id = slot switch
            {
                SkillSlot.AttackA => CharacterSpriteId.AttackA,
                SkillSlot.AttackB => CharacterSpriteId.AttackB,
                SkillSlot.AttackC => CharacterSpriteId.AttackC,
                SkillSlot.SmashSide => CharacterSpriteId.SmashSide,
                _ => CharacterSpriteId.Idle1,
            };
            ForceSprite(id, seconds);
        }

        public void ShowGrabSprite(float seconds)
        {
            ForceSprite(CharacterSpriteId.Grab, seconds);
        }

        public void ShowSmashCharge(float charge01)
        {
            _smashChargeVisual01 = Mathf.Clamp01(charge01);
            _smashChargeVisualTimer = 0.12f;
            ForceSprite(CharacterSpriteId.SmashSide, 0.12f);
        }

        public Sprite GetEffectSprite(SkillSlot slot)
        {
            CharacterSpriteId id = slot switch
            {
                SkillSlot.AttackA => CharacterSpriteId.EffectA,
                SkillSlot.AttackB => CharacterSpriteId.EffectB,
                SkillSlot.AttackC => CharacterSpriteId.EffectC,
                SkillSlot.SmashSide => CharacterSpriteId.EffectSmash,
                _ => CharacterSpriteId.EffectA,
            };
            return _spriteSet.Get(id, null, false);
        }

        void ForceSprite(CharacterSpriteId id, float seconds)
        {
            _forcedSprite = id;
            _forcedSpriteTimer = Mathf.Max(_forcedSpriteTimer, seconds);
        }

        public void SetGrabThrowParameters(GrabParameters grab, ThrowParameters throwData)
        {
            if (grab != null)
            {
                grabParameters = grab;
                grabParameters.startup = Mathf.Clamp(grabParameters.startup, 0.04f, 0.12f);
                grabParameters.recovery = Mathf.Clamp(grabParameters.recovery, 0.08f, 0.22f);
            }
            if (throwData != null) throwParameters = throwData;
        }

        public void ApplyCharacterStats(CharacterStats stats)
        {
            if (stats == null) return;
            moveSpeed = Mathf.Clamp(stats.groundMoveSpeed, 2.5f, 9.5f);
            airMoveSpeed = Mathf.Clamp(stats.airMoveSpeed, 2.0f, 8.5f);
            jumpForce = Mathf.Clamp(stats.jumpForce, 7f, 19f);
            airJumpHeightMultiplier = Mathf.Clamp(stats.airJumpHeightMultiplier, 0.3f, 0.6f);
            walkSpeedRatio = Mathf.Clamp(stats.walkSpeedRatio, 0.2f, 0.5f);
            maxGuardDurability = Mathf.Clamp(stats.guardDurability, 40f, 90f);
            weight = Mathf.Clamp(stats.weight > 0f ? stats.weight : 1f / Mathf.Max(0.6f, stats.lightness), 0.6f, 1.6f);
            groundDodgeDistance = Mathf.Clamp(stats.groundDodgeDistance, 1.2f, 3.8f);
            airDodgeDistance = Mathf.Clamp(stats.airDodgeDistance, 0.8f, 3.2f);
            CurrentGuardDurability = Mathf.Min(CurrentGuardDurability, maxGuardDurability);
            OnGuardChanged?.Invoke(CurrentGuardDurability, maxGuardDurability);
        }

        public bool TryDodge(Vector2 input)
        {
            if (!CanStartDodge()) return false;
            if (_skillExecutor != null && _skillExecutor.IsExecuting) return false;
            bool isAirDodge = !IsGrounded;
            if (isAirDodge && _airDodgeUsed) return false;

            SetGuard(false);
            State = FighterState.Dodging;
            _controlLockTimer = Mathf.Max(_controlLockTimer, dodgeDuration);
            _isAirDodgeActive = isAirDodge;
            float dir = Mathf.Abs(input.x) > 0.35f ? Mathf.Sign(input.x) : 0f;

            if (IsGrounded)
            {
                _dodgeTimer = input.y < -0.35f && dir == 0f ? downDodgeDuration : dodgeDuration;
                if (dir != 0f) IgnoreOpponentCollisionDuringDodge();
                _rb.linearVelocity = new Vector2(dir * (groundDodgeDistance / Mathf.Max(0.05f, _dodgeTimer)), _rb.linearVelocity.y);
                if (dir > 0.1f && !FacingRight) Flip();
                else if (dir < -0.1f && FacingRight) Flip();
            }
            else
            {
                _airDodgeUsed = true;
                _dodgeTimer = dodgeDuration;
                IgnoreOpponentCollisionDuringDodge();
                if (input.sqrMagnitude >= 0.04f)
                {
                    Vector2 airDirection = input.normalized;
                    Vector2 dodgeVelocity = airDirection * (airDodgeDistance / Mathf.Max(0.05f, _dodgeTimer));
                    SuppressDodgeGravity();
                    _rb.linearVelocity = dodgeVelocity;
                }
            }

            ForceSprite(CharacterSpriteId.Dash, _dodgeTimer);
            OnDodged?.Invoke();
            return true;
        }

        void EndAirDodgeOnLanding()
        {
            _isAirDodgeActive = false;
            _dodgeTimer = 0f;
            _controlLockTimer = 0f;
            _forcedSpriteTimer = 0f;
            _forcedSprite = null;
            RestoreDodgeGravity();
            RestoreOpponentCollision();
            State = FighterState.Idle;
        }

        void IgnoreOpponentCollisionDuringDodge()
        {
            if (_bodyCollider == null || Opponent == null) return;
            var opponentCollider = Opponent._bodyCollider != null
                ? Opponent._bodyCollider
                : Opponent.GetComponent<Collider2D>();
            if (opponentCollider == null) return;

            _ignoredOpponentCollider = opponentCollider;
            Physics2D.IgnoreCollision(_bodyCollider, _ignoredOpponentCollider, true);
        }

        void RestoreOpponentCollision()
        {
            _ignoredOpponentCollider = null;
        }

        bool CanStartDodge()
        {
            if (State == FighterState.Dead || State == FighterState.Stunned || State == FighterState.Grabbed || State == FighterState.Dodging) return false;
            if (_isTryingGrab || _heldOpponent != null || _grabbedBy != null) return false;
            if (_guardBreakTimer > 0f || _controlLockTimer > 0f || _skillRecoveryTimer > 0f) return false;
            return true;
        }

        void SuppressDodgeGravity()
        {
            _dodgeGravitySuppressed = true;
            _rb.gravityScale = 0f;
        }

        void RestoreDodgeGravity()
        {
            if (!_dodgeGravitySuppressed) return;
            _dodgeGravitySuppressed = false;
            _rb.gravityScale = _defaultGravityScale;
        }

        public bool TryStartGrab()
        {
            if (!CanAct || Opponent == null) return false;
            if (_grabCooldownTimer > 0f) return false;
            PromptFighters.Audio.GameAudioManager.Instance?.PlayGrab();
            ShowGrabSprite(grabParameters.startup + 0.08f);
            StartCoroutine(ExecuteGrab());
            return true;
        }

        System.Collections.IEnumerator ExecuteGrab()
        {
            _isTryingGrab = true;
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            if (grabParameters.startup > 0f)
                yield return new WaitForSeconds(grabParameters.startup);

            if (State == FighterState.Dead || Opponent == null)
            {
                _isTryingGrab = false;
                yield break;
            }

            bool success = IsOpponentInGrabRange();
            if (success)
            {
                _heldOpponent = Opponent;
                _heldOpponent.BeginGrabbedBy(this);
                _grabHoldTimer = maxGrabHoldSeconds;
            }
            else
            {
                BeginSkillRecovery(grabParameters.recovery);
                ShowGrabSprite(grabParameters.recovery);
            }

            _isTryingGrab = false;
        }

        bool IsOpponentInGrabRange()
        {
            if (Opponent == null) return false;
            Vector2 delta = Opponent.transform.position - transform.position;
            float forward = FacingRight ? delta.x : -delta.x;
            return forward >= 0f &&
                   forward <= grabParameters.range &&
                   Mathf.Abs(delta.y) <= 1.2f;
        }

        void BeginGrabbedBy(Fighter owner)
        {
            if (State == FighterState.Dead) return;
            SetGuard(false);
            _grabbedBy = owner;
            State = FighterState.Grabbed;
            _rb.linearVelocity = Vector2.zero;
            _stunTimer = 0f;
            _controlLockTimer = 0f;
            ForceSprite(CharacterSpriteId.Damage, 0.2f);
        }

        void TickGrab()
        {
            if (_heldOpponent == null) return;

            _grabHoldTimer -= Time.deltaTime;
            _rb.linearVelocity = Vector2.zero;
            ShowGrabSprite(0.12f);
            Vector3 holdOffset = new Vector3(FacingRight ? 0.75f : -0.75f, 0f, 0f);
            _heldOpponent.transform.position = transform.position + holdOffset;
            _heldOpponent._rb.linearVelocity = Vector2.zero;
            _heldOpponent.ForceSprite(CharacterSpriteId.Damage, 0.12f);

            if (_grabHoldTimer <= 0f)
                ReleaseHeldOpponent(applyRecovery: true);
        }

        public bool ThrowHeld(bool forward)
        {
            if (_heldOpponent == null) return false;

            Fighter target = _heldOpponent;
            ReleaseHeldOpponent(applyRecovery: false);

            float direction = FacingRight ? 1f : -1f;
            if (!forward) direction = -direction;

            float damage = forward ? throwParameters.front_damage : throwParameters.back_damage;
            float knockback = forward ? throwParameters.front_knockback : throwParameters.back_knockback;
            float throwForce = knockback * throwKnockbackScale;
            Vector2 kb = new Vector2(direction, 0.25f);
            Vector3 releaseOffset = new Vector3(direction * throwReleaseOffset, 0f, 0f);
            target.transform.position = transform.position + releaseOffset;
            target.TakeDamage(damage, throwForce, kb, 0.15f, damage);
            ShowGrabSprite(0.25f);
            _grabCooldownTimer = Mathf.Max(_grabCooldownTimer, throwGrabCooldown);
            return true;
        }

        public bool ThrowHeldUp()
        {
            if (_heldOpponent == null) return false;

            Fighter target = _heldOpponent;
            ReleaseHeldOpponent(applyRecovery: false);

            float damage = throwParameters.up_damage;
            float throwForce = throwParameters.up_knockback * throwKnockbackScale;
            Vector3 releaseOffset = new Vector3(0f, throwReleaseOffset * 0.8f, 0f);
            target.transform.position = transform.position + releaseOffset;
            target.TakeDamage(damage, throwForce, Vector2.up, 0.15f, damage);
            ShowGrabSprite(0.25f);
            _grabCooldownTimer = Mathf.Max(_grabCooldownTimer, throwGrabCooldown);
            return true;
        }

        void ReleaseHeldOpponent(bool applyRecovery)
        {
            if (_heldOpponent == null) return;
            _heldOpponent._grabbedBy = null;
            if (_heldOpponent.State == FighterState.Grabbed)
                _heldOpponent.State = FighterState.Idle;
            _heldOpponent = null;
            _grabHoldTimer = 0f;
            if (applyRecovery)
            {
                BeginSkillRecovery(grabReleaseRecovery);
                ShowGrabSprite(grabReleaseRecovery);
            }
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
            _smashChargeVisualTimer = 0f;
            _smashChargeVisual01 = 0f;
            _burnTimer          = 0f;
            _slowTimer          = 0f;
            _guardBreakTimer    = 0f;
            _guardRecoveryDelayTimer = 0f;
            _hitFlashTimer      = 0f;
            _heldOpponent       = null;
            _grabbedBy          = null;
            _grabHoldTimer      = 0f;
            _grabCooldownTimer  = 0f;
            _isTryingGrab       = false;
            _dodgeTimer         = 0f;
            _airDodgeUsed       = false;
            _isAirDodgeActive   = false;
            _fastFallUsed       = false;
            _airTime            = 0f;
            RestoreDodgeGravity();
            RestoreOpponentCollision();
            _forcedSprite       = null;
            _forcedSpriteTimer  = 0f;
            _idleAnimTimer      = 0f;
            _idleFrame          = 0;
            _airJumpsRemaining  = maxAirJumps;
            CurrentGuardDurability = maxGuardDurability;
            State               = FighterState.Idle;
            transform.position  = spawnPos;
            _rb.linearVelocity  = Vector2.zero;
            FacingRight = faceRight;
            var s = transform.localScale;
            s.x = faceRight ? Mathf.Abs(s.x) : -Mathf.Abs(s.x);
            transform.localScale = s;
            ApplyVisualScaleCorrection();
            ApplyColliderScaleCorrection();
            OnHPChanged?.Invoke(CurrentHP, maxHP);
            OnGuardChanged?.Invoke(CurrentGuardDurability, maxGuardDurability);
        }

        public void DebugSetBattleStats(float hp, float groundSpeed, float airSpeed,
                                        float jump, float guard, float newWeight,
                                        float newWalkSpeedRatio = -1f,
                                        float newAirJumpHeightMultiplier = -1f)
        {
            maxHP = Mathf.Clamp(hp, 1f, 900f);
            CurrentHP = Mathf.Clamp(CurrentHP, 0f, maxHP);
            moveSpeed = Mathf.Clamp(groundSpeed, 0f, 20f);
            airMoveSpeed = Mathf.Clamp(airSpeed, 0f, 20f);
            jumpForce = Mathf.Clamp(jump, 0f, 30f);
            if (newWalkSpeedRatio >= 0f)
                walkSpeedRatio = Mathf.Clamp(newWalkSpeedRatio, 0.2f, 0.5f);
            if (newAirJumpHeightMultiplier >= 0f)
                airJumpHeightMultiplier = Mathf.Clamp(newAirJumpHeightMultiplier, 0.3f, 0.6f);
            maxGuardDurability = Mathf.Clamp(guard, 1f, 200f);
            CurrentGuardDurability = Mathf.Clamp(CurrentGuardDurability, 0f, maxGuardDurability);
            weight = Mathf.Clamp(newWeight, 0.2f, 3f);
            OnHPChanged?.Invoke(CurrentHP, maxHP);
            OnGuardChanged?.Invoke(CurrentGuardDurability, maxGuardDurability);
        }

        public void DebugSetCurrentHP(float hp)
        {
            CurrentHP = Mathf.Clamp(hp, 0f, maxHP);
            OnHPChanged?.Invoke(CurrentHP, maxHP);
            if (CurrentHP <= 0f) Die();
            else if (State == FighterState.Dead) State = FighterState.Idle;
        }

        public void DebugSetCurrentGuard(float guard)
        {
            CurrentGuardDurability = Mathf.Clamp(guard, 0f, maxGuardDurability);
            OnGuardChanged?.Invoke(CurrentGuardDurability, maxGuardDurability);
        }

        void TickGuard()
        {
            if (State == FighterState.Guarding)
            {
                DamageGuard(guardTimeDrainPerSecond * Time.deltaTime);
                return;
            }

            if (_guardBreakTimer > 0f || _guardRecoveryDelayTimer > 0f) return;
            if (CurrentGuardDurability >= maxGuardDurability) return;

            CurrentGuardDurability = Mathf.Min(maxGuardDurability,
                CurrentGuardDurability + guardRecoveryPerSecond * Time.deltaTime);
            OnGuardChanged?.Invoke(CurrentGuardDurability, maxGuardDurability);
        }

        void TickDodge()
        {
            if (State != FighterState.Dodging) return;
            if (_dodgeTimer > 0f) return;
            RestoreDodgeGravity();
            RestoreOpponentCollision();
            _isAirDodgeActive = false;
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            State = IsGrounded ? FighterState.Idle : FighterState.Falling;
        }

        void DamageGuard(float amount)
        {
            if (amount <= 0f || CurrentGuardDurability <= 0f) return;

            CurrentGuardDurability = Mathf.Max(0f, CurrentGuardDurability - amount);
            _guardRecoveryDelayTimer = guardRecoveryDelay;
            OnGuardChanged?.Invoke(CurrentGuardDurability, maxGuardDurability);
            if (CurrentGuardDurability <= 0f)
                BreakGuard(guardBreakLockDuration);
        }

        void BreakGuard(float duration)
        {
            _guardBreakTimer = Mathf.Max(_guardBreakTimer, duration);
            State = FighterState.Stunned;
            CurrentGuardDurability = 0f;
            _rb.linearVelocity = Vector2.zero;
            OnGuardChanged?.Invoke(CurrentGuardDurability, maxGuardDurability);
            OnGuardBroken?.Invoke();
            DamagePopup.SpawnText(transform.position, "GUARD BREAK", GuardBreakColor, 3.2f);
        }

        void EndGuardBreak()
        {
            _guardBreakTimer = 0f;
            CurrentGuardDurability = Mathf.Max(CurrentGuardDurability, maxGuardDurability * 0.35f);
            _guardRecoveryDelayTimer = guardRecoveryDelay;
            if (State == FighterState.Stunned && _stunTimer <= 0f) State = FighterState.Idle;
            OnGuardChanged?.Invoke(CurrentGuardDurability, maxGuardDurability);
        }

        void SeparateFromOpponent()
        {
            if (Opponent == null || PlayerIndex >= Opponent.PlayerIndex) return;
            if (State == FighterState.Dead || Opponent.State == FighterState.Dead) return;

            // ドッジ中は貫通させる（横回避・空中回避で回り込み可能）
            if (IsDodging || Opponent.IsDodging) return;

            const float minDist    = 0.78f;
            const float pushFadeY  = 0.4f;  // この高さ差以上では押し出しゼロ
            const float maxCorrect = 0.06f; // 1フレームあたりの最大補正量（瞬間移動防止）

            float dx = transform.position.x - Opponent.transform.position.x;
            if (Mathf.Abs(dx) >= minDist) return;

            float dy           = Mathf.Abs(transform.position.y - Opponent.transform.position.y);
            float pushStrength = Mathf.InverseLerp(pushFadeY, 0f, dy); // dy=0 で 1、dy≥pushFadeY で 0
            if (pushStrength <= 0f) return;

            float overlap    = minDist - Mathf.Abs(dx);
            float dir        = dx >= 0f ? 1f : -1f;
            float correction = Mathf.Min(overlap * 0.5f * pushStrength, maxCorrect);
            var   shift      = new Vector3(dir * correction, 0f, 0f);
            transform.position          += shift;
            Opponent.transform.position -= shift;
        }

        void ClampToStage()
        {
            var bm = BattleManager.Instance;
            if (bm == null || !bm.IsFighting) return;

            float minX = bm.StageMinX;
            float maxX = bm.StageMaxX;
            Vector3 pos = transform.position;
            float clampedX = Mathf.Clamp(pos.x, minX, maxX);
            if (Mathf.Approximately(pos.x, clampedX)) return;

            transform.position = new Vector3(clampedX, pos.y, pos.z);
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        }

        public void SetCharacterSprite(Sprite sprite)
        {
            EnsureVisualRenderer();
            if (_sprite == null) return;
            _sprite.sprite = sprite;
            _spriteSet.Set(CharacterSpriteId.Idle1, sprite);
            ApplyVisualScaleCorrection();
        }

        public void SetCharacterSprites(CharacterSpriteSet spriteSet)
        {
            EnsureVisualRenderer();
            if (_sprite == null || spriteSet == null) return;
            _spriteSet = spriteSet;
            Sprite primary = _spriteSet.Get(CharacterSpriteId.Idle1, _sprite.sprite);
            if (primary != null) _sprite.sprite = primary;
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
                _spriteSet.Set(CharacterSpriteId.Idle1, _sprite.sprite);
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

        void ApplyColliderScaleCorrection()
        {
            var col = GetComponent<BoxCollider2D>();
            if (col == null) return;
            col.size = new Vector2(0.78f, 1.75f);
            col.offset = new Vector2(0f, 0.82f);
        }

        void Die()
        {
            RestoreDodgeGravity();
            State = FighterState.Dead;
            _rb.linearVelocity = Vector2.zero;
            OnDeath?.Invoke();
        }

        void Flip()
        {
            FacingRight = !FacingRight;
            var s = transform.localScale;
            s.x *= -1f;
            transform.localScale = s;
        }

        // ====== ギミック用メソッド ======

        public void HealHP(float amount)
        {
            if (State == FighterState.Dead) return;
            CurrentHP = Mathf.Min(maxHP, CurrentHP + amount);
            OnHPChanged?.Invoke(CurrentHP, maxHP);
            DamagePopup.SpawnText(transform.position, $"+{amount:0}", new Color(0.4f, 1f, 0.4f), 1.6f);
        }

        public void StartTemporarySpeedChange(float multiplier, float duration)
            => StartCoroutine(TemporarySpeedChange(multiplier, duration));

        public void StartTemporaryJumpChange(float multiplier, float duration)
            => StartCoroutine(TemporaryJumpChange(multiplier, duration));

        public void StartTemporaryDamageBoost(float multiplier, float duration)
            => StartCoroutine(TemporaryDamageBoost(multiplier, duration));

        public void StartTemporaryInvincible(float duration)
            => StartCoroutine(TemporaryInvincible(duration));

        public void StartTemporaryChaos(float duration)
            => StartCoroutine(TemporaryChaos(duration));

        System.Collections.IEnumerator TemporarySpeedChange(float multiplier, float duration)
        {
            float origGround = moveSpeed;
            float origAir    = airMoveSpeed;
            moveSpeed    = origGround * multiplier;
            airMoveSpeed = origAir * multiplier;
            yield return new WaitForSeconds(duration);
            moveSpeed    = origGround;
            airMoveSpeed = origAir;
        }

        System.Collections.IEnumerator TemporaryJumpChange(float multiplier, float duration)
        {
            float orig = jumpForce;
            jumpForce  = orig * multiplier;
            yield return new WaitForSeconds(duration);
            jumpForce  = orig;
        }

        System.Collections.IEnumerator TemporaryDamageBoost(float multiplier, float duration)
        {
            DamageMultiplier = multiplier;
            yield return new WaitForSeconds(duration);
            DamageMultiplier = 1f;
        }

        System.Collections.IEnumerator TemporaryInvincible(float duration)
        {
            IsInvincible = true;
            yield return new WaitForSeconds(duration);
            IsInvincible = false;
        }

        System.Collections.IEnumerator TemporaryChaos(float duration)
        {
            InputReversed = true;
            yield return new WaitForSeconds(duration);
            InputReversed = false;
        }

        void OnDrawGizmosSelected()
        {
            if (groundCheck == null) return;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
    }
}
