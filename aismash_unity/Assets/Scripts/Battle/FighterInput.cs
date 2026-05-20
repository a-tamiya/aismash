using UnityEngine;
using UnityEngine.InputSystem;
using PromptFighters.Audio;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    [RequireComponent(typeof(Fighter))]
    public class FighterInput : MonoBehaviour
    {
        public int playerIndex = 0; // 0 = 1P, 1 = 2P

        Fighter _fighter;
        SkillExecutor _skills;
        bool _smashHeld;
        float _smashCharge;
        readonly float[] _skillChargeTimers  = new float[4];
        readonly bool[]  _skillCharging      = new bool[4];
        readonly float[] _lastSkillPressTime = { -10f, -10f, -10f, -10f };
        float _lastSmashFlickTime = -10f;
        float _previousSmashAxis;
        Vector2 _previousDodgeInput;
        bool _previousGuardHeld;
        bool _wasHoldingOpponent;
        bool _throwInputReleasedAfterGrab = true;
        float _airSkillFaceTimer;
        float _recentJumpTimer;
        const float MaxSmashCharge = 1f;
        const float AirSkillFaceWindow = 0.22f;
        const float RecentJumpWindow = 0.2f;
        const float SmashFlickWindow = 0.18f;
        const float DodgeInputThreshold = 0.35f;
        const float ThrowInputThreshold = 0.35f;
        const float ThrowNeutralThreshold = 0.2f;
        const float ShortHopThreshold = -0.35f;
        const float FastFallThreshold = -0.55f;
        const float MinChargeReleaseSeconds = 0.08f;

        void Awake()
        {
            _fighter = GetComponent<Fighter>();
            _fighter.PlayerIndex = playerIndex;
            _skills  = GetComponent<SkillExecutor>();
        }

        void OnDisable()
        {
            if (_fighter != null)
                GameAudioManager.Instance?.SetGroundMove(_fighter, false);
        }

        void Update()
        {
            if (_airSkillFaceTimer > 0f) _airSkillFaceTimer -= Time.deltaTime;
            if (_recentJumpTimer   > 0f) _recentJumpTimer   -= Time.deltaTime;

            if (_fighter.State == FighterState.Dead)
            {
                GameAudioManager.Instance?.SetGroundMove(_fighter, false);
                return;
            }

            var bm = BattleManager.Instance;
            // 対戦中またはトレーニング中だけ操作を受け付ける
            if (bm != null && !bm.IsFighting)
            {
                GameAudioManager.Instance?.SetGroundMove(_fighter, false);
                return;
            }

            if (!_fighter.IsHoldingOpponent)
            {
                _wasHoldingOpponent = false;
                _throwInputReleasedAfterGrab = true;
            }

            if (_fighter.IsGrabbed)
            {
                GameAudioManager.Instance?.SetGroundMove(_fighter, false);
                return;
            }
            if (_fighter.IsHoldingOpponent)
            {
                GameAudioManager.Instance?.SetGroundMove(_fighter, false);
                Vector2 throwInput = ReadMoveVector();
                float throwDir = throwInput.x;
                if (!_wasHoldingOpponent)
                {
                    _wasHoldingOpponent = true;
                    _throwInputReleasedAfterGrab = throwInput.magnitude < ThrowNeutralThreshold;
                }

                if (!_throwInputReleasedAfterGrab)
                {
                    if (throwInput.magnitude < ThrowNeutralThreshold)
                        _throwInputReleasedAfterGrab = true;
                    return;
                }

                if (throwInput.y > ThrowInputThreshold &&
                    throwInput.y >= Mathf.Abs(throwInput.x))
                    _fighter.ThrowHeldUp();
                else if (throwInput.y < -ThrowInputThreshold &&
                    -throwInput.y >= Mathf.Abs(throwInput.x))
                    _fighter.ThrowHeldDown();
                else if (throwDir > ThrowInputThreshold) _fighter.ThrowHeld(_fighter.FacingRight);
                else if (throwDir < -ThrowInputThreshold) _fighter.ThrowHeld(!_fighter.FacingRight);
                return;
            }

            RecordSmashFlick();
            bool guardHeld = ReadGuard();
            // AttackAがチャージ技のときはJキーをスマッシュに使わせない
            bool attackAChargeable = _skills?.GetSkill(SkillSlot.AttackA)?.chargeable == true;
            if (attackAChargeable) { _smashHeld = false; _smashCharge = 0f; }
            float smashMultiplier = (guardHeld || attackAChargeable) ? 0f : UpdateSmashCharge();
            if (guardHeld)
            {
                _smashHeld = false;
                _smashCharge = 0f;
            }
            if (_smashHeld)
            {
                _fighter.ShowSmashCharge(_smashCharge / MaxSmashCharge);
                _fighter.Move(0f);
                _fighter.SetGuard(false);
                _previousGuardHeld = guardHeld;
                return;
            }

            if (_skills != null && !guardHeld && HandleChargeSkillInput())
            {
                _fighter.Move(0f);
                _fighter.SetGuard(false);
                GameAudioManager.Instance?.SetGroundMove(_fighter, false);
                _previousGuardHeld = guardHeld;
                return;
            }

            Vector2 moveInput = ReadMoveVector();
            bool jumpPressed = ReadJumpPressed();
            bool grabPressed = ReadGrabPressed();

            if (guardHeld && _fighter.IsGrounded && (jumpPressed || grabPressed))
            {
                _fighter.SetGuard(false);
                if (jumpPressed) JumpFromInput(moveInput);
                else _fighter.TryStartGrab();
                _previousDodgeInput = moveInput;
                _previousGuardHeld = guardHeld;
                return;
            }

            if (ShouldStartDodge(moveInput, guardHeld) && _fighter.TryDodge(moveInput))
            {
                _previousDodgeInput = moveInput;
                _previousGuardHeld = guardHeld;
                return;
            }
            _previousDodgeInput = moveInput;

            float moveX = _fighter.InputReversed ? -moveInput.x : moveInput.x;
            _fighter.Move(moveX);
            GameAudioManager.Instance?.SetGroundMove(
                _fighter,
                Mathf.Abs(moveX) > 0.18f && _fighter.IsGrounded && _fighter.CanAct);
            _fighter.SetGuard(guardHeld && _fighter.IsGrounded);
            if (jumpPressed) JumpFromInput(moveInput);
            if (!_fighter.IsGrounded && moveInput.y <= FastFallThreshold)
                _fighter.FastFall();
            if (grabPressed) _fighter.TryStartGrab();
            _previousGuardHeld = guardHeld;

            // スキルはEnded以外で使用可能
            if (_skills != null && !guardHeld)
            {
                bool skillA = ReadSkillPressed(SkillSlot.AttackA);
                bool skillB = ReadSkillPressed(SkillSlot.AttackB);
                bool skillC = ReadSkillPressed(SkillSlot.AttackC);
                bool smash  = smashMultiplier > 0f;

                // 押下時刻を常に記録（派生バッファ用）
                if (skillA) _lastSkillPressTime[(int)SkillSlot.AttackA] = Time.time;
                if (skillB) _lastSkillPressTime[(int)SkillSlot.AttackB] = Time.time;
                if (skillC) _lastSkillPressTime[(int)SkillSlot.AttackC] = Time.time;

                if (!_fighter.IsGrounded)
                {
                    if (skillA || skillB || skillC || smash)
                        _airSkillFaceTimer = AirSkillFaceWindow;
                    if (_airSkillFaceTimer > 0f)
                        _fighter.FaceTowardInput(moveX);
                }

                // follow_up: 派生元スロットの「現在押下 or バッファ内押下」で受け付ける
                bool followUsed = false;
                if (_skills.IsFollowUpReady)
                {
                    var fs  = _skills.FollowUpSlot;
                    int fi  = (int)fs;
                    bool currentPress = (fs == SkillSlot.AttackA && skillA) ||
                                        (fs == SkillSlot.AttackB && skillB) ||
                                        (fs == SkillSlot.AttackC && skillC);
                    bool bufferedPress = Time.time - _lastSkillPressTime[fi] < 0.5f;
                    if (currentPress || bufferedPress)
                    {
                        _lastSkillPressTime[fi] = -10f; // バッファ消費
                        followUsed = _skills.TryExecuteFollowUp();
                    }
                }
                if (!followUsed)
                {
                    HandleSkillSlot(SkillSlot.AttackA, skillA);
                    HandleSkillSlot(SkillSlot.AttackB, skillB);
                    HandleSkillSlot(SkillSlot.AttackC, skillC);
                    if (smash) _skills.TryUseSkill(SkillSlot.SmashSide, smashMultiplier);
                }
            }
        }

        void JumpFromInput(Vector2 moveInput)
        {
            bool shortHop = _fighter.IsGrounded && moveInput.y <= ShortHopThreshold;
            _fighter.Jump(shortHop ? _fighter.shortHopMultiplier : 1f);
            _recentJumpTimer = RecentJumpWindow;
        }

        float ReadMove()
        {
            float dir = 0f;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (playerIndex == 0)
                {
                    if (kb.aKey.isPressed) dir -= 1f;
                    if (kb.dKey.isPressed) dir += 1f;
                }
                else
                {
                    if (kb.leftArrowKey.isPressed)  dir -= 1f;
                    if (kb.rightArrowKey.isPressed) dir += 1f;
                }
            }
            else
            {
                if (playerIndex == 0)
                {
                    if (LegacyKey(KeyCode.A)) dir -= 1f;
                    if (LegacyKey(KeyCode.D)) dir += 1f;
                }
                else
                {
                    if (LegacyKey(KeyCode.LeftArrow))  dir -= 1f;
                    if (LegacyKey(KeyCode.RightArrow)) dir += 1f;
                }
            }

            var gp = GetGamepad();
            if (gp != null)
            {
                float stick = gp.leftStick.x.ReadValue();
                float dpad  = gp.dpad.x.ReadValue();
                float gpDir = Mathf.Abs(stick) > 0.3f ? stick : dpad;
                if (Mathf.Abs(gpDir) > 0.1f) dir = gpDir;
            }

            return Mathf.Clamp(dir, -1f, 1f);
        }

        Vector2 ReadMoveVector()
        {
            var gp = GetGamepad();
            if (gp != null)
            {
                Vector2 stick = gp.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.01f)
                    return Vector2.ClampMagnitude(stick, 1f);

                Vector2 dpad = gp.dpad.ReadValue();
                if (dpad.sqrMagnitude > 0.01f)
                    return Vector2.ClampMagnitude(dpad, 1f);
            }

            return new Vector2(ReadMove(), ReadVertical());
        }

        float ReadVertical()
        {
            float dir = 0f;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (playerIndex == 0)
                {
                    if (kb.sKey.isPressed) dir -= 1f;
                    if (kb.wKey.isPressed) dir += 1f;
                }
                else
                {
                    if (kb.downArrowKey.isPressed) dir -= 1f;
                    if (kb.upArrowKey.isPressed)   dir += 1f;
                }
            }
            else
            {
                if (playerIndex == 0)
                {
                    if (LegacyKey(KeyCode.S)) dir -= 1f;
                    if (LegacyKey(KeyCode.W)) dir += 1f;
                }
                else
                {
                    if (LegacyKey(KeyCode.DownArrow)) dir -= 1f;
                    if (LegacyKey(KeyCode.UpArrow))   dir += 1f;
                }
            }

            var gp = GetGamepad();
            if (gp != null)
            {
                float stick = gp.leftStick.y.ReadValue();
                float dpad  = gp.dpad.y.ReadValue();
                float gpDir = Mathf.Abs(stick) > 0.3f ? stick : dpad;
                if (Mathf.Abs(gpDir) > 0.1f) dir = gpDir;
            }

            return Mathf.Clamp(dir, -1f, 1f);
        }

        bool ShouldStartDodge(Vector2 input, bool guardHeld)
        {
            if (!guardHeld) return false;

            bool horizontalPressed = Mathf.Abs(input.x) >= DodgeInputThreshold &&
                                     Mathf.Abs(_previousDodgeInput.x) < DodgeInputThreshold;
            bool verticalPressed = Mathf.Abs(input.y) >= DodgeInputThreshold &&
                                   Mathf.Abs(_previousDodgeInput.y) < DodgeInputThreshold;

            if (_fighter.IsGrounded)
            {
                bool downPressed = input.y <= -DodgeInputThreshold &&
                                   _previousDodgeInput.y > -DodgeInputThreshold;
                return _previousGuardHeld && (horizontalPressed || downPressed);
            }

            if (ReadDodgePressed()) return true;
            bool dirHeld = input.sqrMagnitude >= DodgeInputThreshold * DodgeInputThreshold;
            // 直近ジャンプ後はガード+方向を押したまま待つだけで発動（絶）
            if (_recentJumpTimer > 0f && dirHeld) return true;
            return dirHeld && _previousGuardHeld && (horizontalPressed || verticalPressed);
        }

        bool ReadJumpPressed()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (playerIndex == 0 && kb.wKey.wasPressedThisFrame)        return true;
                if (playerIndex == 1 && kb.upArrowKey.wasPressedThisFrame)  return true;
            }
            else
            {
                if (playerIndex == 0 && LegacyKeyDown(KeyCode.W)) return true;
                if (playerIndex == 1 && LegacyKeyDown(KeyCode.UpArrow)) return true;
            }
            var gp = GetGamepad();
            return gp != null && gp.buttonNorth.wasPressedThisFrame;
        }

        bool ReadGuard()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (playerIndex == 0 && kb.leftShiftKey.isPressed)  return true;
                if (playerIndex == 1 && kb.rightCtrlKey.isPressed) return true;
            }
            else
            {
                if (playerIndex == 0 && LegacyKey(KeyCode.LeftShift)) return true;
                if (playerIndex == 1 && LegacyKey(KeyCode.RightControl)) return true;
            }
            var gp = GetGamepad();
            return gp != null && (gp.rightShoulder.isPressed || gp.rightTrigger.isPressed);
        }

        bool ReadDodgePressed()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (playerIndex == 0 && kb.leftShiftKey.wasPressedThisFrame)  return true;
                if (playerIndex == 1 && kb.rightCtrlKey.wasPressedThisFrame) return true;
            }
            else
            {
                if (playerIndex == 0 && LegacyKeyDown(KeyCode.LeftShift)) return true;
                if (playerIndex == 1 && LegacyKeyDown(KeyCode.RightControl)) return true;
            }

            var gp = GetGamepad();
            return gp != null && (gp.rightShoulder.wasPressedThisFrame || gp.rightTrigger.wasPressedThisFrame);
        }

        bool ReadGrabPressed()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (playerIndex == 0 && kb.gKey.wasPressedThisFrame) return true;
                if (playerIndex == 1 && kb.numpad0Key.wasPressedThisFrame) return true;
            }
            else
            {
                if (playerIndex == 0 && LegacyKeyDown(KeyCode.G)) return true;
                if (playerIndex == 1 && LegacyKeyDown(KeyCode.Keypad0)) return true;
            }

            var gp = GetGamepad();
            return gp != null && (gp.leftShoulder.wasPressedThisFrame || gp.leftTrigger.wasPressedThisFrame);
        }

        void HandleSkillSlot(SkillSlot slot, bool pressed)
        {
            var skillDef = _skills?.GetSkill(slot);
            if (IsChargeSkill(skillDef))
                return;

            if (pressed) _skills.TryUseSkill(slot);
        }

        bool ReadSkillHeld(SkillSlot slot)
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (playerIndex == 0)
                    switch (slot)
                    {
                        case SkillSlot.AttackA: return kb.jKey.isPressed;
                        case SkillSlot.AttackB: return kb.kKey.isPressed;
                        case SkillSlot.AttackC: return kb.lKey.isPressed;
                    }
                else
                    switch (slot)
                    {
                        case SkillSlot.AttackA: return kb.numpad2Key.isPressed;
                        case SkillSlot.AttackB: return kb.numpad3Key.isPressed;
                        case SkillSlot.AttackC: return kb.numpad1Key.isPressed;
                    }
            }
            else
            {
                if (playerIndex == 0)
                    switch (slot)
                    {
                        case SkillSlot.AttackA: return LegacyKey(KeyCode.J);
                        case SkillSlot.AttackB: return LegacyKey(KeyCode.K);
                        case SkillSlot.AttackC: return LegacyKey(KeyCode.L);
                    }
                else
                    switch (slot)
                    {
                        case SkillSlot.AttackA: return LegacyKey(KeyCode.Keypad2);
                        case SkillSlot.AttackB: return LegacyKey(KeyCode.Keypad3);
                        case SkillSlot.AttackC: return LegacyKey(KeyCode.Keypad1);
                    }
            }
            var gp = GetGamepad();
            if (gp == null) return false;
            return slot switch
            {
                SkillSlot.AttackA => gp.buttonEast.isPressed,
                SkillSlot.AttackB => gp.buttonSouth.isPressed,
                SkillSlot.AttackC => gp.buttonWest.isPressed,
                _ => false,
            };
        }

        bool ReadSkillReleased(SkillSlot slot)
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (playerIndex == 0)
                    switch (slot)
                    {
                        case SkillSlot.AttackA: return kb.jKey.wasReleasedThisFrame;
                        case SkillSlot.AttackB: return kb.kKey.wasReleasedThisFrame;
                        case SkillSlot.AttackC: return kb.lKey.wasReleasedThisFrame;
                    }
                else
                    switch (slot)
                    {
                        case SkillSlot.AttackA: return kb.numpad2Key.wasReleasedThisFrame;
                        case SkillSlot.AttackB: return kb.numpad3Key.wasReleasedThisFrame;
                        case SkillSlot.AttackC: return kb.numpad1Key.wasReleasedThisFrame;
                    }
            }
            else
            {
                if (playerIndex == 0)
                    switch (slot)
                    {
                        case SkillSlot.AttackA: return Input.GetKeyUp(KeyCode.J);
                        case SkillSlot.AttackB: return Input.GetKeyUp(KeyCode.K);
                        case SkillSlot.AttackC: return Input.GetKeyUp(KeyCode.L);
                    }
                else
                    switch (slot)
                    {
                        case SkillSlot.AttackA: return Input.GetKeyUp(KeyCode.Keypad2);
                        case SkillSlot.AttackB: return Input.GetKeyUp(KeyCode.Keypad3);
                        case SkillSlot.AttackC: return Input.GetKeyUp(KeyCode.Keypad1);
                    }
            }
            var gp = GetGamepad();
            if (gp == null) return false;
            return slot switch
            {
                SkillSlot.AttackA => gp.buttonEast.wasReleasedThisFrame,
                SkillSlot.AttackB => gp.buttonSouth.wasReleasedThisFrame,
                SkillSlot.AttackC => gp.buttonWest.wasReleasedThisFrame,
                _ => false,
            };
        }

        bool ReadSkillPressed(SkillSlot slot)
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (playerIndex == 0)
                {
                    switch (slot)
                    {
                        case SkillSlot.AttackA: if (kb.jKey.wasPressedThisFrame) return true; break;
                        case SkillSlot.AttackB: if (kb.kKey.wasPressedThisFrame) return true; break;
                        case SkillSlot.AttackC: if (kb.lKey.wasPressedThisFrame) return true; break;
                    }
                }
                else
                {
                    switch (slot)
                    {
                        case SkillSlot.AttackA: if (kb.numpad2Key.wasPressedThisFrame) return true; break;
                        case SkillSlot.AttackB: if (kb.numpad3Key.wasPressedThisFrame) return true; break;
                        case SkillSlot.AttackC: if (kb.numpad1Key.wasPressedThisFrame) return true; break;
                    }
                }
            }
            else
            {
                if (playerIndex == 0)
                {
                    switch (slot)
                    {
                        case SkillSlot.AttackA: if (LegacyKeyDown(KeyCode.J)) return true; break;
                        case SkillSlot.AttackB: if (LegacyKeyDown(KeyCode.K)) return true; break;
                        case SkillSlot.AttackC: if (LegacyKeyDown(KeyCode.L)) return true; break;
                    }
                }
                else
                {
                    switch (slot)
                    {
                        case SkillSlot.AttackA: if (LegacyKeyDown(KeyCode.Keypad2)) return true; break;
                        case SkillSlot.AttackB: if (LegacyKeyDown(KeyCode.Keypad3)) return true; break;
                        case SkillSlot.AttackC: if (LegacyKeyDown(KeyCode.Keypad1)) return true; break;
                    }
                }
            }

            var gp = GetGamepad();
            if (gp != null)
            {
                switch (slot)
                {
                    case SkillSlot.AttackA: return gp.buttonEast.wasPressedThisFrame;  // B / Circle
                    case SkillSlot.AttackB: return gp.buttonSouth.wasPressedThisFrame; // A / Cross
                    case SkillSlot.AttackC: return gp.buttonWest.wasPressedThisFrame;  // X / Square
                }
            }

            return false;
        }

        float UpdateSmashCharge()
        {
            bool rightButtonPressed = false;
            var kb = Keyboard.current;
            if (kb != null)
            {
                rightButtonPressed |= playerIndex == 0
                    ? kb.jKey.isPressed
                    : kb.numpad2Key.isPressed;
            }
            else
            {
                rightButtonPressed |= playerIndex == 0
                    ? LegacyKey(KeyCode.J)
                    : LegacyKey(KeyCode.Keypad2);
            }

            var gp = GetGamepad();
            if (gp != null)
            {
                rightButtonPressed |= gp.buttonEast.isPressed;
            }

            bool canStartSmash = Time.time - _lastSmashFlickTime <= SmashFlickWindow;
            bool charging = _smashHeld
                ? rightButtonPressed
                : rightButtonPressed && canStartSmash;

            if (charging)
            {
                _smashHeld = true;
                _smashCharge = Mathf.Min(MaxSmashCharge, _smashCharge + Time.deltaTime);
                if (_smashCharge >= MaxSmashCharge)
                {
                    _smashHeld = false;
                    _smashCharge = 0f;
                    return 2f;
                }
                return 0f;
            }

            bool released = _smashHeld;
            float multiplier = released ? Mathf.Lerp(1f, 2f, _smashCharge / MaxSmashCharge) : 0f;
            _smashHeld = false;
            _smashCharge = 0f;
            return multiplier;
        }

        void RecordSmashFlick()
        {
            float axis = 0f;
            var kb = Keyboard.current;
            if (kb != null)
            {
                bool keyboardFlick = playerIndex == 0
                    ? kb.aKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame
                    : kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame;
                if (keyboardFlick) _lastSmashFlickTime = Time.time;
            }
            else
            {
                bool keyboardFlick = playerIndex == 0
                    ? LegacyKeyDown(KeyCode.A) || LegacyKeyDown(KeyCode.D)
                    : LegacyKeyDown(KeyCode.LeftArrow) || LegacyKeyDown(KeyCode.RightArrow);
                if (keyboardFlick) _lastSmashFlickTime = Time.time;
            }

            var gp = GetGamepad();
            if (gp != null)
            {
                float stick = gp.leftStick.x.ReadValue();
                float dpad = gp.dpad.x.ReadValue();
                axis = Mathf.Abs(stick) > Mathf.Abs(dpad) ? stick : dpad;
            }

            if (Mathf.Abs(axis) >= 0.7f && Mathf.Abs(_previousSmashAxis) < 0.7f)
                _lastSmashFlickTime = Time.time;
            _previousSmashAxis = axis;
        }

        Gamepad GetGamepad()
        {
            int active = 0;
            foreach (var gp in Gamepad.all)
            {
                if (gp.lastUpdateTime <= 0) continue; // ゴーストデバイスをスキップ
                if (active == playerIndex) return gp;
                active++;
            }
            return null;
        }

        static bool LegacyKey(KeyCode key)
        {
            try { return Input.GetKey(key); }
            catch (System.InvalidOperationException) { return false; }
        }

        static bool LegacyKeyDown(KeyCode key)
        {
            try { return Input.GetKeyDown(key); }
            catch (System.InvalidOperationException) { return false; }
        }

        bool HandleChargeSkillInput()
        {
            bool handled = false;
            handled |= HandleChargeSkillSlot(SkillSlot.AttackA);
            handled |= HandleChargeSkillSlot(SkillSlot.AttackB);
            handled |= HandleChargeSkillSlot(SkillSlot.AttackC);
            return handled;
        }

        bool HandleChargeSkillSlot(SkillSlot slot)
        {
            int i = (int)slot;
            var skillDef = _skills?.GetSkill(slot);
            if (!IsChargeSkill(skillDef))
                return false;

            float maxCharge = Mathf.Clamp(skillDef.max_charge_time > 0f ? skillDef.max_charge_time : 1.5f, 1.0f, 3.0f);
            bool held = ReadSkillHeld(slot);
            bool pressed = ReadSkillPressed(slot);
            bool released = ReadSkillReleased(slot);

            if ((pressed || held) && !_skillCharging[i])
            {
                if (_skills.IsExecuting || !_fighter.CanAct)
                    return false;

                _skillCharging[i] = true;
                _skillChargeTimers[i] = 0f;
                _fighter.ShowSkillCharge(0f);
                return true;
            }

            if (!_skillCharging[i])
                return false;

            if (_skills.IsExecuting || !_fighter.CanAct)
            {
                _skillCharging[i] = false;
                _skillChargeTimers[i] = 0f;
                return false;
            }

            _skillChargeTimers[i] += Time.deltaTime;
            float chargeRate = Mathf.Clamp01(_skillChargeTimers[i] / maxCharge);
            _fighter.ShowSkillCharge(chargeRate);

            bool canRelease = _skillChargeTimers[i] >= MinChargeReleaseSeconds;
            if ((released || !held) && canRelease)
            {
                float chargeLevel = Mathf.Clamp01(_skillChargeTimers[i] / maxCharge);
                _skills.TryUseSkill(slot, 1f + chargeLevel * 0.8f);
                _skillCharging[i] = false;
                _skillChargeTimers[i] = 0f;
                return true;
            }

            if (_skillChargeTimers[i] >= maxCharge)
            {
                _skills.TryUseSkill(slot, 1.8f);
                _skillCharging[i] = false;
                _skillChargeTimers[i] = 0f;
            }

            return true;
        }

        static bool IsChargeSkill(SkillData skill)
        {
            return skill != null && (skill.chargeable || skill.max_charge_time > 0f);
        }
    }
}
