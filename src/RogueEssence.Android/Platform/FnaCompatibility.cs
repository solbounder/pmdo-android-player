using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System.Linq;

namespace RogueEssence
{
    public static class FNALoggerEXT
    {
        public static Action<string> LogInfo { get; set; }
        public static Action<string> LogWarn { get; set; }
        public static Action<string> LogError { get; set; }
    }

    public static class PlatformGamePad
    {
        private static readonly Buttons[] PhysicalButtons =
        {
            Buttons.A, Buttons.B, Buttons.X, Buttons.Y,
            Buttons.Back, Buttons.Start, Buttons.BigButton,
            Buttons.LeftShoulder, Buttons.RightShoulder,
            Buttons.LeftStick, Buttons.RightStick,
            Buttons.DPadUp, Buttons.DPadDown, Buttons.DPadLeft, Buttons.DPadRight
        };

        public static GamePadState GetState(PlayerIndex playerIndex)
        {
            GamePadState physical = Microsoft.Xna.Framework.Input.GamePad.GetState(playerIndex);
            TouchController.SetPhysicalConnected(physical.IsConnected);
            if (physical.IsConnected && HasPhysicalActivity(physical))
                TouchController.UsePhysicalInput();
            if (physical.IsConnected && !TouchController.IsTouchMode)
                return physical;

            Buttons[] pressed = TouchController.PressedButtons();
            return new GamePadState(Vector2.Zero, Vector2.Zero, 0f, 0f, pressed);
        }

        private static bool HasPhysicalActivity(GamePadState state)
        {
            foreach (Buttons button in PhysicalButtons)
                if (state.IsButtonDown(button))
                    return true;

            const float stickThresholdSquared = 0.16f;
            return state.ThumbSticks.Left.LengthSquared() > stickThresholdSquared ||
                state.ThumbSticks.Right.LengthSquared() > stickThresholdSquared ||
                state.Triggers.Left > 0.45f || state.Triggers.Right > 0.45f;
        }

        public static string GetGUIDEXT(PlayerIndex playerIndex)
        {
            GamePadCapabilities capabilities = Microsoft.Xna.Framework.Input.GamePad.GetCapabilities(playerIndex);
            string name = capabilities.DisplayName;
            return string.IsNullOrWhiteSpace(name) ? "android-gamepad" : name;
        }
    }

    public static class TouchController
    {
        private static readonly object Sync = new object();
        private static readonly HashSet<Buttons> Pressed = new HashSet<Buttons>();
        private static bool physicalConnected;
        private static bool touchMode = true;
        public static event Action<bool> PhysicalControllerChanged;
        public static event Action<bool> TouchModeChanged;

        public static bool IsTouchMode
        {
            get { lock (Sync) return touchMode; }
        }

        public static void Set(Buttons button, bool down)
        {
            if (down) UseTouchInput();
            lock (Sync)
            {
                if (down) Pressed.Add(button); else Pressed.Remove(button);
            }
        }

        public static void Clear()
        {
            lock (Sync) Pressed.Clear();
        }

        internal static Buttons[] PressedButtons()
        {
            lock (Sync) return Pressed.ToArray();
        }

        internal static void SetPhysicalConnected(bool connected)
        {
            if (physicalConnected == connected) return;
            physicalConnected = connected;
            PhysicalControllerChanged?.Invoke(connected);
            if (!connected) UseTouchInput();
        }

        public static void UseTouchInput() => SetTouchMode(true);

        public static void UsePhysicalInput() => SetTouchMode(false);

        private static void SetTouchMode(bool enabled)
        {
            Action<bool> changed;
            lock (Sync)
            {
                if (touchMode == enabled) return;
                touchMode = enabled;
                if (!enabled) Pressed.Clear();
                changed = TouchModeChanged;
            }
            changed?.Invoke(enabled);
        }
    }
}
