using UnityEngine;
using UnityEngine.InputSystem;

namespace PromptFighters.Battle
{
    [RequireComponent(typeof(Fighter))]
    public class FighterInput : MonoBehaviour
    {
        public int playerIndex = 0; // 0 = 1P, 1 = 2P

        Fighter _fighter;

        void Awake()
        {
            _fighter = GetComponent<Fighter>();
            _fighter.PlayerIndex = playerIndex;
        }

        void Update()
        {
            if (_fighter.State == FighterState.Dead) return;

            _fighter.Move(ReadMove());
            _fighter.SetGuard(ReadGuard());
            if (ReadJumpPressed()) _fighter.Jump();
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

        Gamepad GetGamepad()
        {
            var all = Gamepad.all;
            return all.Count > playerIndex ? all[playerIndex] : null;
        }
    }
}
