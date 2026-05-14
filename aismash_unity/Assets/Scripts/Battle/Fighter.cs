using UnityEngine;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;

namespace PromptFighters.Battle
{
    public enum FighterState { Idle, Moving, Jumping, Falling, Guarding, Stunned, Grabbed, Dead }

    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public class Fighter : MonoBehaviour
    {
        [Header("Stats")]
        public float maxHP = 100f;

        [Header("Movement")]
        public float moveSpeed = 5f;
        public float airMoveSpeed = 4f;
        public float jumpForce = 12f;
        [Range(0.6f, 1.6f)] public float weight = 1f;

        [Header("Guard")]
        [Range(0f, 1f)] public float guardDamageRatio = 0.15f;
        public float maxGuardDurability = 100f;
        public float guardTimeDrainPerSecond = 18f;
        public float guardHitDamageRatio = 0.45f;
        public float guardBreakLockDuration = 5f;

        [Header("Grab / Throw")]
        public GrabParameters grabParameters = new GrabParameters();
        public ThrowParameters throwParameters = new ThrowParameters();
        public float maxGrabHoldSeconds = 3f;
        public float grabReleaseRecovery = 0.35f;
        public float throwGrabCooldown = 1.0f;
        [Range(0.1f, 1f)] public float throwKnockbackScale = 0.55f;

        [Header("Ground Check")]
        public Transform groundCheck;
        public float groundCheckRadius = 0.12f;
        public LayerMask groundLayer;

        // 相手ファイターへの参照（BattleManagerがセット）
        [HideInInspector] public Fighter Opponent;

        public float CurrentHP { get; private set; }
        public float CurrentGuardDurability { get; private set; }
        public FighterState State { get; private set; } = FighterState.Idle;
        public bool IsGrounded { get; private set; }
        public bool FacingRight { get; set; } = true;
        public int PlayerIndex { get; set; }
        public bool IsHoldingOpponent => _heldOpponent != null;
        public bool IsGrabbed => _grabbedBy != null;

        public event System.Action<float, float> OnHPChanged;
        public event System.Action<float, float> OnGuardChanged;
        public event System.Action               OnGuardBroken;
        public event System.Action               OnDeath;
        public event System.Action<float, bool>  OnDamageReceived; // (damage, wasBlocked)

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

        // 状態異常タイマー
        float _burnTimer;
        float _burnTickTimer;
        float _burnDamagePerTick = 2f;
        float _slowTimer;
        float _slowFactor = 0.5f;
        float _guardBreakTimer;
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

        public bool CanAct =>
            State != FighterState.Guarding &&
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
            _rootSprite     = GetComponent<SpriteRenderer>();
            EnsureVisualRenderer();
            _skillExecutor  = GetComponent<SkillExecutor>();
            CurrentHP       = maxHP;
            CurrentGuardDurability = maxGuardDurability;
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
            if (_grabCooldownTimer  > 0f) _grabCooldownTimer  -= Time.deltaTime;
            if (_slowTimer          > 0f) _slowTimer          -= Time.deltaTime;
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

            UpdateState();
            UpdateVisual();
        }

        void LateUpdate()
        {
            ClampToStage();
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
            if (State == FighterState.Dead || State == FighterState.Guarding || State == FighterState.Grabbed) return;

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

            if (State != FighterState.Guarding && State != FighterState.Stunned)
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
            if (!CanAct) return;
            float baseSpeed = IsGrounded ? moveSpeed : airMoveSpeed;
            float speed = baseSpeed * (_slowTimer > 0f ? _slowFactor : 1f);
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
            if (_skillExecutor != null && _skillExecutor.IsExecuting) return;
            if (_heldOpponent != null || _grabbedBy != null || _isTryingGrab) return;
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
            if (_grabbedBy != null) return;

            bool blocking = State == FighterState.Guarding && _guardBreakTimer <= 0f;
            float actual  = blocking ? Mathf.Max(damage * guardDamageRatio, guardDamage) : damage;
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
            if (grab != null) grabParameters = grab;
            if (throwData != null) throwParameters = throwData;
        }

        public void ApplyCharacterStats(CharacterStats stats)
        {
            if (stats == null) return;
            moveSpeed = Mathf.Clamp(stats.groundMoveSpeed, 3.2f, 7.5f);
            airMoveSpeed = Mathf.Clamp(stats.airMoveSpeed, 2.5f, 6.5f);
            jumpForce = Mathf.Clamp(stats.jumpForce, 8f, 16f);
            maxGuardDurability = Mathf.Clamp(stats.guardDurability, 60f, 140f);
            weight = Mathf.Clamp(stats.weight > 0f ? stats.weight : 1f / Mathf.Max(0.6f, stats.lightness), 0.6f, 1.6f);
            CurrentGuardDurability = Mathf.Min(CurrentGuardDurability, maxGuardDurability);
            OnGuardChanged?.Invoke(CurrentGuardDurability, maxGuardDurability);
        }

        public bool TryStartGrab()
        {
            if (!CanAct || Opponent == null) return false;
            if (_grabCooldownTimer > 0f) return false;
            ShowGrabSprite(grabParameters.startup + 0.15f);
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
        }

        void TickGrab()
        {
            if (_heldOpponent == null) return;

            _grabHoldTimer -= Time.deltaTime;
            _rb.linearVelocity = Vector2.zero;
            Vector3 holdOffset = new Vector3(FacingRight ? 0.75f : -0.75f, 0f, 0f);
            _heldOpponent.transform.position = transform.position + holdOffset;
            _heldOpponent._rb.linearVelocity = Vector2.zero;

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
            target.TakeDamage(damage, throwForce, kb, 0.15f, damage);
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
            if (applyRecovery) BeginSkillRecovery(grabReleaseRecovery);
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
            _heldOpponent       = null;
            _grabbedBy          = null;
            _grabHoldTimer      = 0f;
            _grabCooldownTimer  = 0f;
            _isTryingGrab       = false;
            _forcedSprite       = null;
            _forcedSpriteTimer  = 0f;
            _idleAnimTimer      = 0f;
            _idleFrame          = 0;
            CurrentGuardDurability = maxGuardDurability;
            State               = FighterState.Idle;
            transform.position  = spawnPos;
            _rb.linearVelocity  = Vector2.zero;
            FacingRight = faceRight;
            var s = transform.localScale;
            s.x = faceRight ? Mathf.Abs(s.x) : -Mathf.Abs(s.x);
            transform.localScale = s;
            ApplyVisualScaleCorrection();
            OnHPChanged?.Invoke(CurrentHP, maxHP);
            OnGuardChanged?.Invoke(CurrentGuardDurability, maxGuardDurability);
        }

        void TickGuard()
        {
            if (State != FighterState.Guarding) return;
            DamageGuard(guardTimeDrainPerSecond * Time.deltaTime);
        }

        void DamageGuard(float amount)
        {
            if (amount <= 0f || CurrentGuardDurability <= 0f) return;

            CurrentGuardDurability = Mathf.Max(0f, CurrentGuardDurability - amount);
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
            CurrentGuardDurability = maxGuardDurability;
            if (State == FighterState.Stunned && _stunTimer <= 0f) State = FighterState.Idle;
            OnGuardChanged?.Invoke(CurrentGuardDurability, maxGuardDurability);
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

        void Die()
        {
            State = FighterState.Dead;
            _rb.linearVelocity = Vector2.zero;
            OnDeath?.Invoke();
        }

        void AutoFaceOpponent()
        {
            if (Opponent == null) return;
            if (State == FighterState.Dead || State == FighterState.Stunned || State == FighterState.Grabbed) return;
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
