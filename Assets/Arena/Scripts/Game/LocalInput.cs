// Reads a gamepad and the keyboard into an InputCommand for the simulation.
// Button presses are remembered until the next simulation tick consumes them, so a quick tap is never lost.
using Arena.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Arena.Game
{
    public class LocalInput
    {
        bool dashPending, abilityPending, ballPending;
        float rumbleT;

        public bool UsingGamepad { get; private set; }

        /// <summary>Call once per rendered frame to collect button presses.</summary>
        public void Poll(Hero hero)
        {
            var gp = Gamepad.current; var kb = Keyboard.current;
            if (gp != null)
            {
                if (gp.buttonEast.wasPressedThisFrame) dashPending = true;
                if (gp.buttonNorth.wasPressedThisFrame) abilityPending = true;
                if (gp.selectButton.wasPressedThisFrame) ballPending = true;
                if (gp.wasUpdatedThisFrame && (gp.leftStick.ReadValue().sqrMagnitude > 0.1f || AnyButton(gp))) UsingGamepad = true;
            }
            if (kb != null)
            {
                if (kb.kKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame) dashPending = true;
                if (hero != Hero.Curve && (kb.uKey.wasPressedThisFrame || kb.digit1Key.wasPressedThisFrame)) abilityPending = true;
                if (kb.rKey.wasPressedThisFrame) ballPending = true;
                if (kb.anyKey.wasPressedThisFrame) UsingGamepad = false;
            }
        }

        static bool AnyButton(Gamepad gp) =>
            gp.buttonSouth.isPressed || gp.buttonEast.isPressed || gp.buttonWest.isPressed || gp.buttonNorth.isPressed ||
            gp.leftShoulder.isPressed || gp.rightShoulder.isPressed || gp.startButton.isPressed;

        /// <summary>Builds the command for one simulation tick (held buttons + presses since the last tick).</summary>
        public InputCommand Take(Hero hero)
        {
            var gp = Gamepad.current; var kb = Keyboard.current;
            Vector2 stick = Vector2.zero;
            bool kick = false, lob = false, cl = false, cr = false, call = false;
            if (gp != null)
            {
                stick = gp.leftStick.ReadValue();
                var dp = gp.dpad.ReadValue(); if (dp.sqrMagnitude > 0.1f) stick = dp;
                kick |= gp.buttonSouth.isPressed; lob |= gp.buttonWest.isPressed;
                cl |= gp.leftShoulder.isPressed; cr |= gp.rightShoulder.isPressed;
                call |= gp.rightTrigger.ReadValue() > 0.4f || gp.leftTrigger.ReadValue() > 0.4f; // RT / LT — «дай пас»
            }
            if (kb != null)
            {
                float kx = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1 : 0) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1 : 0);
                float ky = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1 : 0) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1 : 0);
                if (kx != 0 || ky != 0) stick = new Vector2(kx, ky);
                kick |= kb.jKey.isPressed || kb.spaceKey.isPressed;
                lob |= kb.lKey.isPressed || kb.digit4Key.isPressed;
                call |= kb.qKey.isPressed || kb.eKey.isPressed; // Q / E — «дай пас»
                if (hero == Hero.Curve) { cl |= kb.uKey.isPressed || kb.digit1Key.isPressed; cr |= kb.iKey.isPressed || kb.digit2Key.isPressed; }
            }
            if (stick.sqrMagnitude > 1) stick.Normalize();
            var f = W.StickToField(stick);
            var c = new InputCommand
            {
                MoveX = f.x, MoveY = f.y, Kick = kick, Lob = lob, CurveLeft = cl, CurveRight = cr,
                Dash = dashPending, Ability = abilityPending, BallToMe = ballPending, CallPass = call,
            };
            dashPending = abilityPending = ballPending = false;
            return c;
        }

        public void ClearPending() { dashPending = abilityPending = ballPending = false; }

        public void Rumble(float strength)
        {
            var gp = Gamepad.current; if (gp == null) return;
            gp.SetMotorSpeeds(strength * 0.6f, strength);
            rumbleT = 0.06f + strength * 0.18f;
        }

        public void UpdateRumble(float dt)
        {
            if (rumbleT <= 0) return;
            rumbleT -= dt;
            if (rumbleT <= 0) Gamepad.current?.SetMotorSpeeds(0, 0);
        }

        public void StopRumble() { rumbleT = 0; Gamepad.current?.SetMotorSpeeds(0, 0); }
    }
}
