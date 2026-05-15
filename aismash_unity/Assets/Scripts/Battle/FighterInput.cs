using UnityEngine;
using UnityEngine.InputSystem;
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
        float _lastSmashFlickTime = -10f;
        float _previousSmashAxis;
        const float MaxSmashCharge = 1f;
        const float SmashFlickWindow = 0.18f;

        void Awake()
        {
            _fighter = GetComponent<Fighter>();
            _fighter.PlayerIndex = playerIndex;
            _skills  = GetComponent<SkillExecutor>();
        }

        void Update()
        {
            if (_fighter.State == FighterState.Dead) return;

            var bm = BattleManager.Instance;
            // 対戦中またはトレーニング中だけ操作を受け付ける
            if (bm != null && !bm.IsFighting) return;

            if (_fighter.IsGrabbed) return;
            if (_fighter.IsHoldingOpponent)
            {
                float throwDir = ReadMove();
                if (throwDir > 0.35f) _fighter.ThrowHeld(_fighter.FacingRight);
                else if (throwDir < -0.35f) _fighter.ThrowHeld(!_fighter.FacingRight);
                return;
            }

            RecordSmashFlick();
            float smashMultiplier = UpdateSmashCharge();
            if (_smashHeld)
            {
                _fighter.ShowSmashCharge(_smashCharge / MaxSmashCharge);
                _fighter.Move(0f);
                _fighter.SetGuard(false);
                return;
            }

            float move = ReadMove();
            float vertical = ReadVertical();
            bool wantsDodge = !_fighter.IsGrounded || Mathf.Abs(move) > 0.35f || vertical < -0.35f;
            if (wantsDodge && ReadDodgePressed() && _fighter.TryDodge(move, vertical))
                return;

            _fighter.Move(move);
            _fighter.SetGuard(ReadGuard());
            if (ReadJumpPressed()) _fighter.Jump();
            if (ReadGrabPressed()) _fighter.TryStartGrab();

            // スキルはEnded以外で使用可能
            if (_skills != null)
            {
                if (ReadSkillPressed(SkillSlot.AttackA)) _skills.TryUseSkill(SkillSlot.AttackA);
                if (ReadSkillPressed(SkillSlot.AttackB)) _skills.TryUseSkill(SkillSlot.AttackB);
                if (ReadSkillPressed(SkillSlot.AttackC)) _skills.TryUseSkill(SkillSlot.AttackC);
                if (smashMultiplier > 0f) _skills.TryUseSkill(SkillSlot.SmashSide, smashMultiplier);
            }
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

        bool ReadJumpPressed()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (playerIndex == 0 && kb.wKey.wasPressedThisFrame)        return true;
                if (playerIndex == 1 && kb.upArrowKey.wasPressedThisFrame)  return true;
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
                if (playerIndex == 1 && kb.rightShiftKey.isPressed) return true;
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
                if (playerIndex == 1 && kb.rightShiftKey.wasPressedThisFrame) return true;
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

            var gp = GetGamepad();
            return gp != null && (gp.leftShoulder.wasPressedThisFrame || gp.leftTrigger.wasPressedThisFrame);
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
            var all = Gamepad.all;
            return all.Count > playerIndex ? all[playerIndex] : null;
        }
    }
}
