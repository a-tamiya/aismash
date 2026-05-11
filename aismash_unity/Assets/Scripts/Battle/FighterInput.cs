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

        void Awake()
        {
            _fighter = GetComponent<Fighter>();
            _fighter.PlayerIndex = playerIndex;
            _skills  = GetComponent<SkillExecutor>();
        }

        void Update()
        {
            if (_fighter.State == FighterState.Dead) return;

            _fighter.Move(ReadMove());
            _fighter.SetGuard(ReadGuard());
            if (ReadJumpPressed()) _fighter.Jump();

            if (_skills != null)
            {
                if (ReadSkillPressed(SkillSlot.Close))    _skills.TryUseSkill(SkillSlot.Close);
                if (ReadSkillPressed(SkillSlot.Ranged))   _skills.TryUseSkill(SkillSlot.Ranged);
                if (ReadSkillPressed(SkillSlot.Special))  _skills.TryUseSkill(SkillSlot.Special);
                if (ReadSkillPressed(SkillSlot.Ultimate)) _skills.TryUseSkill(SkillSlot.Ultimate);
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

        bool ReadJumpPressed()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (playerIndex == 0 && kb.wKey.wasPressedThisFrame)        return true;
                if (playerIndex == 1 && kb.upArrowKey.wasPressedThisFrame)  return true;
            }
            var gp = GetGamepad();
            return gp != null && gp.buttonSouth.wasPressedThisFrame;
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
            return gp != null && gp.leftShoulder.isPressed;
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
                        case SkillSlot.Close:    if (kb.jKey.wasPressedThisFrame) return true; break;
                        case SkillSlot.Ranged:   if (kb.kKey.wasPressedThisFrame) return true; break;
                        case SkillSlot.Special:  if (kb.lKey.wasPressedThisFrame) return true; break;
                        case SkillSlot.Ultimate: if (kb.iKey.wasPressedThisFrame) return true; break;
                    }
                }
                else
                {
                    switch (slot)
                    {
                        case SkillSlot.Close:    if (kb.numpad1Key.wasPressedThisFrame) return true; break;
                        case SkillSlot.Ranged:   if (kb.numpad2Key.wasPressedThisFrame) return true; break;
                        case SkillSlot.Special:  if (kb.numpad3Key.wasPressedThisFrame) return true; break;
                        case SkillSlot.Ultimate: if (kb.numpad5Key.wasPressedThisFrame) return true; break;
                    }
                }
            }

            var gp = GetGamepad();
            if (gp != null)
            {
                switch (slot)
                {
                    case SkillSlot.Close:    return gp.buttonWest.wasPressedThisFrame;  // X / Square
                    case SkillSlot.Ranged:   return gp.buttonNorth.wasPressedThisFrame; // Y / Triangle
                    case SkillSlot.Special:  return gp.buttonEast.wasPressedThisFrame;  // B / Circle
                    case SkillSlot.Ultimate: return gp.rightShoulder.wasPressedThisFrame;
                }
            }

            return false;
        }

        Gamepad GetGamepad()
        {
            var all = Gamepad.all;
            return all.Count > playerIndex ? all[playerIndex] : null;
        }
    }
}
