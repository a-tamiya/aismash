using UnityEngine;
using PromptFighters.UI;

namespace PromptFighters.Battle
{
    // Fighterの一時的バフ/デバフ・ギミック系状態異常をまとめた部分クラス。
    // コア戦闘ロジック(Fighter.cs)と分離して見通しを良くするための分割。
    public partial class Fighter
    {
        // バリアで吸収できる残りダメージ量（TakeDamage/TakeThrowで消費）。
        float _barrierHP;

        // 一定量のダメージを吸収するシールド（barrier アクション用）。
        public void StartBarrier(float amount, float duration)
            => StartCoroutine(BarrierRoutine(amount, duration));

        System.Collections.IEnumerator BarrierRoutine(float amount, float duration)
        {
            _barrierHP = Mathf.Max(_barrierHP, amount);
            DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f, "BARRIER!",
                new Color(0.4f, 0.8f, 1f), 2.2f);
            BattleLogger.Instance?.LogEvent($"{PlayerLabel()}がバリア展開");

            var aura = CreateBarrierAura();
            var auraSr = aura != null ? aura.GetComponent<SpriteRenderer>() : null;
            float elapsed = 0f;
            // 効果中はオーラを表示。時間切れ or 吸収しきりで終了。
            while (elapsed < duration && _barrierHP > 0f && State != FighterState.Dead)
            {
                if (auraSr != null)
                {
                    float pulse = (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f;
                    auraSr.color = new Color(0.4f, 0.8f, 1f, 0.20f + 0.18f * pulse);
                    aura.transform.localScale = Vector3.one * ((2.2f + 0.12f * pulse) * _charSizeScale);
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
            _barrierHP = 0f;
            if (aura != null) Destroy(aura);
        }

        GameObject CreateBarrierAura()
        {
            var go = new GameObject("BarrierAura");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.75f * _charSizeScale, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Skills.RuntimeSprite.Circle();
            sr.color = new Color(0.4f, 0.8f, 1f, 0.3f);
            sr.sortingOrder = 8; // キャラの少し手前に半透明で重ねる
            go.transform.localScale = Vector3.one * (2.2f * _charSizeScale);
            return go;
        }

        // 一定時間、相手を中心点へ継続的に引き寄せる（gravity_well アクション用）。
        public void StartGravityWell(Vector2 center, float radius, float force, float duration)
            => StartCoroutine(GravityWellRoutine(center, radius, force, duration));

        System.Collections.IEnumerator GravityWellRoutine(Vector2 center, float radius, float force, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration && State != FighterState.Dead)
            {
                var opp = Opponent;
                if (opp != null && opp.State != FighterState.Dead)
                {
                    Vector2 d = center - (Vector2)opp.transform.position;
                    if (d.magnitude <= radius)
                        opp.AddExternalForce(d.normalized * force);
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        // ── ステータス系：永続倍率（ギミック）＋一時変化（スキル）の二層構成 ──
        // 永続倍率はラウンドをまたいで保持し（ResetForBattleで再適用）、後の指示で上書き（後勝ち）。
        // 一時変化（スキル）は永続倍率の上に乗算し、終了後は永続倍率値へ戻す。
        [System.NonSerialized] public float PermSpeedMult   = 1f;
        [System.NonSerialized] public float PermJumpMult    = 1f;
        [System.NonSerialized] public float PermDamageMult  = 1f;
        [System.NonSerialized] public float PermGravityMult = 1f;
        [System.NonSerialized] public float PermSizeMult    = 1f;
        float _moveSpeedBase, _airMoveSpeedBase; bool _speedBaseSet;
        float _jumpForceBase; bool _jumpBaseSet;
        Vector3 _visualBaseScale; bool _sizeBaseSet;
        Coroutine _speedTempCo, _jumpTempCo, _damageTempCo;

        void EnsureSpeedBase() { if (!_speedBaseSet) { _moveSpeedBase = moveSpeed; _airMoveSpeedBase = airMoveSpeed; _speedBaseSet = true; } }
        void EnsureJumpBase()  { if (!_jumpBaseSet)  { _jumpForceBase = jumpForce; _jumpBaseSet = true; } }
        void EnsureSizeBase()  { EnsureVisualRenderer(); if (!_sizeBaseSet && _visualRoot != null) { _visualBaseScale = _visualRoot.localScale; _sizeBaseSet = true; } }

        void ShowStatPopup(string label, Color col)
            => DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f, label, col, 2.2f);

        // 永続（ギミック）。後勝ちで倍率を置き換え即反映。一時効果は打ち消す。
        public void ApplyPermanentSpeed(float mult)
        {
            EnsureSpeedBase(); PermSpeedMult = mult;
            if (_speedTempCo != null) { StopCoroutine(_speedTempCo); _speedTempCo = null; }
            moveSpeed = _moveSpeedBase * mult; airMoveSpeed = _airMoveSpeedBase * mult;
            _speedBoostTimer = mult > 1f ? 99999f : 0f;
            ShowStatPopup(mult >= 1f ? "SPEED UP!" : "SPEED DOWN!", mult >= 1f ? SpeedBoostColor : SlowColor);
        }
        public void ApplyPermanentJump(float mult)
        {
            EnsureJumpBase(); PermJumpMult = mult;
            if (_jumpTempCo != null) { StopCoroutine(_jumpTempCo); _jumpTempCo = null; }
            jumpForce = _jumpForceBase * mult;
            _jumpBoostTimer = mult > 1f ? 99999f : 0f;
            ShowStatPopup(mult >= 1f ? "JUMP UP!" : "JUMP DOWN!", mult >= 1f ? JumpBoostColor : SlowColor);
        }
        public void ApplyPermanentDamage(float mult)
        {
            PermDamageMult = mult;
            if (_damageTempCo != null) { StopCoroutine(_damageTempCo); _damageTempCo = null; }
            DamageMultiplier = mult;
            ShowStatPopup(mult >= 1f ? "POWER UP!" : "POWER DOWN!", mult >= 1f ? DamageBoostColor : SlowColor);
        }
        public void ApplyPermanentGravity(float mult)
        {
            PermGravityMult = mult;
            if (!_dodgeGravitySuppressed) _rb.gravityScale = _defaultGravityScale * mult;
            ShowStatPopup(mult > 1f ? "HEAVY!" : "FLOAT!", mult > 1f ? new Color(0.6f, 0.4f, 1f) : new Color(0.5f, 1f, 0.9f));
        }
        public void ApplyPermanentSize(float mult)
        {
            EnsureSizeBase(); if (_visualRoot == null) return; PermSizeMult = mult;
            _visualRoot.localScale = new Vector3(_visualBaseScale.x * mult, _visualBaseScale.y * mult, _visualBaseScale.z);
            ShowStatPopup(mult > 1f ? "BIG!" : "SMALL!", new Color(1f, 0.85f, 0.2f));
        }

        // 永続倍率を再適用（ResetForBattleからラウンド開始時に呼び、ラウンドをまたいで保持する）
        public void ReapplyPermanentStats()
        {
            if (_speedBaseSet) { moveSpeed = _moveSpeedBase * PermSpeedMult; airMoveSpeed = _airMoveSpeedBase * PermSpeedMult; }
            if (_jumpBaseSet)  jumpForce = _jumpForceBase * PermJumpMult;
            DamageMultiplier = PermDamageMult;
            if (!_dodgeGravitySuppressed) _rb.gravityScale = _defaultGravityScale * PermGravityMult;
            if (_sizeBaseSet && _visualRoot != null)
                _visualRoot.localScale = new Vector3(_visualBaseScale.x * PermSizeMult, _visualBaseScale.y * PermSizeMult, _visualBaseScale.z);
            if (PermSpeedMult > 1f) _speedBoostTimer = 99999f;
            if (PermJumpMult  > 1f) _jumpBoostTimer  = 99999f;
        }

        // 一時（スキル）。永続倍率の上に乗算し、終了後は永続倍率値へ戻す。後勝ち（前の一時効果は打ち消す）。
        public void StartTemporarySpeedChange(float multiplier, float duration)
        {
            EnsureSpeedBase();
            if (_speedTempCo != null) StopCoroutine(_speedTempCo);
            _speedTempCo = StartCoroutine(TemporarySpeedChange(multiplier, duration));
        }
        public void StartTemporaryJumpChange(float multiplier, float duration)
        {
            EnsureJumpBase();
            if (_jumpTempCo != null) StopCoroutine(_jumpTempCo);
            _jumpTempCo = StartCoroutine(TemporaryJumpChange(multiplier, duration));
        }
        public void StartTemporaryDamageBoost(float multiplier, float duration)
        {
            if (_damageTempCo != null) StopCoroutine(_damageTempCo);
            _damageTempCo = StartCoroutine(TemporaryDamageBoost(multiplier, duration));
        }

        public void StartTemporaryInvincible(float duration)
            => StartCoroutine(TemporaryInvincible(duration));

        public void StartTemporaryChaos(float duration)
            => StartCoroutine(TemporaryChaos(duration));

        public void StartTemporaryReflect(float duration)
        {
            _reflectTimer = Mathf.Max(_reflectTimer, duration);
            DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f, "REFLECT!", ReflectColor, 2.2f);
        }

        public void StartCounter(float duration, float damage, float knockback, Vector2 kbDir, float stun)
        {
            _counterTimer    = Mathf.Max(_counterTimer, duration);
            _counterDamage   = damage;
            _counterKnockback= knockback;
            _counterKbDir    = kbDir;
            _counterStun     = stun;
            DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f, "COUNTER!", CounterColor, 1.5f);
            BattleLogger.Instance?.LogEvent($"{PlayerLabel()}がカウンター構え");
        }

        System.Collections.IEnumerator TemporarySpeedChange(float multiplier, float duration)
        {
            _speedBoostTimer = duration;
            moveSpeed    = _moveSpeedBase * PermSpeedMult * multiplier;
            airMoveSpeed = _airMoveSpeedBase * PermSpeedMult * multiplier;
            ShowStatPopup(multiplier >= 1f ? "SPEED UP!" : "SPEED DOWN!", multiplier >= 1f ? SpeedBoostColor : SlowColor);
            yield return new WaitForSeconds(duration);
            moveSpeed    = _moveSpeedBase * PermSpeedMult;
            airMoveSpeed = _airMoveSpeedBase * PermSpeedMult;
            _speedTempCo = null;
        }

        System.Collections.IEnumerator TemporaryJumpChange(float multiplier, float duration)
        {
            if (multiplier >= 1f) _jumpBoostTimer = duration;
            jumpForce = _jumpForceBase * PermJumpMult * multiplier;
            ShowStatPopup(multiplier >= 1f ? "JUMP UP!" : "JUMP DOWN!", multiplier >= 1f ? JumpBoostColor : SlowColor);
            yield return new WaitForSeconds(duration);
            jumpForce = _jumpForceBase * PermJumpMult;
            _jumpTempCo = null;
        }

        System.Collections.IEnumerator TemporaryDamageBoost(float multiplier, float duration)
        {
            DamageMultiplier = PermDamageMult * multiplier;
            ShowStatPopup(multiplier >= 1f ? "POWER UP!" : "POWER DOWN!", multiplier >= 1f ? DamageBoostColor : SlowColor);
            yield return new WaitForSeconds(duration);
            DamageMultiplier = PermDamageMult;
            _damageTempCo = null;
        }

        System.Collections.IEnumerator TemporaryInvincible(float duration)
        {
            IsInvincible = true;
            _transparencyTimer = Mathf.Max(_transparencyTimer, duration);
            DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f, "INVINCIBLE!", new Color(1f, 1f, 0.3f), 2.2f);
            yield return new WaitForSeconds(duration);
            IsInvincible = false;
        }

        System.Collections.IEnumerator TemporaryChaos(float duration)
        {
            InputReversed = true;
            DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f, "CHAOS!", ChaosColor, 2.2f);
            yield return new WaitForSeconds(duration);
            InputReversed = false;
        }

        // ── 新ギミック用メソッド ────────────────────────────────────────

        public void StartTemporaryWind(float force, float duration)
            => StartCoroutine(TemporaryWind(force, duration));

        public void StartTemporaryFloorLava(float dpsRatio, float duration)
            => StartCoroutine(TemporaryFloorLava(dpsRatio, duration));

        public void StartTemporaryGuardDisable(float duration)
        {
            _guardDisabled = true;
            _guardDisabledTimer = Mathf.Max(_guardDisabledTimer, duration);
            if (State == FighterState.Guarding) SetGuard(false);
            DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f, "NO GUARD!", GuardDisableColor, 2.2f);
            BattleLogger.Instance?.LogEvent($"{PlayerLabel()}のガード封印");
        }

        public void StartTemporarySkillSeal(int slot, float duration)
        {
            _sealedSlot = Mathf.Clamp(slot, 0, 3);
            _sealedSlotTimer = Mathf.Max(_sealedSlotTimer, duration);
            DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f, "SEALED!", SealColor, 2.2f);
            BattleLogger.Instance?.LogEvent($"{PlayerLabel()}のスロット{_sealedSlot}封印");
        }

        public void StartTemporarySuperKnockback(float duration)
            => StartCoroutine(TemporarySuperKnockback(duration));

        System.Collections.IEnumerator TemporaryWind(float force, float duration)
        {
            DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f, "WIND!", WindColor, 2.2f);
            float elapsed = 0f;
            while (elapsed < duration && State != FighterState.Dead)
            {
                _rb.AddForce(new Vector2(force, 0f), ForceMode2D.Force);
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        System.Collections.IEnumerator TemporaryFloorLava(float dpsRatio, float duration)
        {
            float elapsed  = 0f;
            float tickTimer = 0f;
            float dps = maxHP * dpsRatio;
            DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f, "FLOOR LAVA!", LavaColor, 2.2f);
            while (elapsed < duration && State != FighterState.Dead)
            {
                elapsed    += Time.deltaTime;
                if (IsGrounded)
                {
                    tickTimer += Time.deltaTime;
                    if (tickTimer >= 0.5f)
                    {
                        tickTimer = 0f;
                        float dmg = dps * 0.5f;
                        CurrentHP = Mathf.Max(0f, CurrentHP - dmg);
                        OnHPChanged?.Invoke(CurrentHP, maxHP);
                        DamagePopup.Spawn(transform.position, dmg, false);
                        if (CurrentHP <= 0f) { Die(); yield break; }
                    }
                }
                else tickTimer = 0f;
                yield return null;
            }
        }

        System.Collections.IEnumerator TemporarySuperKnockback(float duration)
        {
            if (_superKnockbackOrigWeight < 0f) _superKnockbackOrigWeight = weight;
            weight = Mathf.Max(0.2f, weight * 0.3f);
            DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f, "FRAGILE!", SuperKBColor, 2.2f);
            yield return new WaitForSeconds(duration);
            if (_superKnockbackOrigWeight >= 0f)
            {
                weight = _superKnockbackOrigWeight;
                _superKnockbackOrigWeight = -1f;
            }
        }
    }
}
