using System;
using Ludots.Platform.Abstractions;
using Raylib_cs;

namespace Ludots.Raylib.Render
{
    public static class RaylibInputPathParser
    {
        public static KeyboardKey? ParseKeyboardKey(string path)
        {
            // Expected format: "<Keyboard>/w" or "<Keyboard>/space"
            if (!path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase)) return null;

            string keyName = path.Substring(11).ToUpper();
            
            // Map common keys
            if (keyName.Length == 1)
            {
                // Single char keys (A-Z, 0-9)
                char c = keyName[0];
                if (c >= 'A' && c <= 'Z') return (KeyboardKey)((int)KeyboardKey.KEY_A + (c - 'A'));
                if (c >= '0' && c <= '9') return (KeyboardKey)((int)KeyboardKey.KEY_ZERO + (c - '0'));
            }

            if (keyName.Length >= 2 &&
                keyName[0] == 'F' &&
                int.TryParse(keyName.AsSpan(1), out int fNum) &&
                fNum >= 1 && fNum <= 12)
            {
                return (KeyboardKey)((int)KeyboardKey.KEY_F1 + (fNum - 1));
            }

            return keyName switch
            {
                "SPACE" => KeyboardKey.KEY_SPACE,
                "ENTER" => KeyboardKey.KEY_ENTER,
                "ESCAPE" => KeyboardKey.KEY_ESCAPE,
                "TAB" => KeyboardKey.KEY_TAB,
                "BACKSPACE" => KeyboardKey.KEY_BACKSPACE,
                "INSERT" => KeyboardKey.KEY_INSERT,
                "DELETE" => KeyboardKey.KEY_DELETE,
                "PAGEUP" => KeyboardKey.KEY_PAGE_UP,
                "PAGEDOWN" => KeyboardKey.KEY_PAGE_DOWN,
                "HOME" => KeyboardKey.KEY_HOME,
                "END" => KeyboardKey.KEY_END,
                "MINUS" => KeyboardKey.KEY_MINUS,
                "EQUAL" => KeyboardKey.KEY_EQUAL,
                "EQUALS" => KeyboardKey.KEY_EQUAL,
                "LEFT" => KeyboardKey.KEY_LEFT,
                "RIGHT" => KeyboardKey.KEY_RIGHT,
                "UP" => KeyboardKey.KEY_UP,
                "DOWN" => KeyboardKey.KEY_DOWN,
                "LEFTSHIFT" => KeyboardKey.KEY_LEFT_SHIFT,
                "LEFTCONTROL" => KeyboardKey.KEY_LEFT_CONTROL,
                "LEFTALT" => KeyboardKey.KEY_LEFT_ALT,
                "RIGHTSHIFT" => KeyboardKey.KEY_RIGHT_SHIFT,
                "RIGHTCONTROL" => KeyboardKey.KEY_RIGHT_CONTROL,
                "RIGHTCTRL" => KeyboardKey.KEY_RIGHT_CONTROL,
                "RIGHTALT" => KeyboardKey.KEY_RIGHT_ALT,
                "LEFTCTRL" => KeyboardKey.KEY_LEFT_CONTROL,
                _ => null
            };
        }

        public static MouseButton? ParseMouseButton(string path)
        {
            if (!path.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase)) return null;
            string btnName = path.Substring(8).ToUpper();

            return btnName switch
            {
                "LEFTBUTTON" => MouseButton.MOUSE_LEFT_BUTTON,
                "RIGHTBUTTON" => MouseButton.MOUSE_RIGHT_BUTTON,
                "MIDDLEBUTTON" => MouseButton.MOUSE_MIDDLE_BUTTON,
                _ => null
            };
        }

        /// <summary>
        /// Slot from a <c>&lt;Gamepad&gt;</c> / <c>&lt;GamepadN&gt;</c> tag. Returns -1 when the path is not a gamepad path.
        /// An illegal slot or an unknown control throws.
        /// </summary>
        public static int ParseGamepadIndex(string path)
        {
            if (!GamepadControlPath.TryParse(path, out GamepadControlPath parsed))
            {
                return -1;
            }

            return parsed.Pad;
        }

        public static GamepadButton? ParseGamepadButton(string path)
        {
            if (!GamepadControlPath.TryParse(path, out GamepadControlPath parsed))
            {
                return null;
            }

            if (parsed.Kind is GamepadControlKind.Button or GamepadControlKind.Trigger)
            {
                return ToRaylibButton(parsed.Button);
            }

            return null;
        }

        public static (int Pad, GamepadAxis Axis)? ParseGamepadAxis(string path)
        {
            if (!GamepadControlPath.TryParse(path, out GamepadControlPath parsed))
            {
                return null;
            }

            if (parsed.Kind is GamepadControlKind.StickAxis or GamepadControlKind.Trigger)
            {
                return (parsed.Pad, ToRaylibAxis(parsed.Axis));
            }

            return null;
        }

        public static GamepadButton ToRaylibButton(GamepadButtonControl button) => button switch
        {
            GamepadButtonControl.LeftFaceUp => GamepadButton.GAMEPAD_BUTTON_LEFT_FACE_UP,
            GamepadButtonControl.LeftFaceRight => GamepadButton.GAMEPAD_BUTTON_LEFT_FACE_RIGHT,
            GamepadButtonControl.LeftFaceDown => GamepadButton.GAMEPAD_BUTTON_LEFT_FACE_DOWN,
            GamepadButtonControl.LeftFaceLeft => GamepadButton.GAMEPAD_BUTTON_LEFT_FACE_LEFT,
            GamepadButtonControl.RightFaceUp => GamepadButton.GAMEPAD_BUTTON_RIGHT_FACE_UP,
            GamepadButtonControl.RightFaceRight => GamepadButton.GAMEPAD_BUTTON_RIGHT_FACE_RIGHT,
            GamepadButtonControl.RightFaceDown => GamepadButton.GAMEPAD_BUTTON_RIGHT_FACE_DOWN,
            GamepadButtonControl.RightFaceLeft => GamepadButton.GAMEPAD_BUTTON_RIGHT_FACE_LEFT,
            GamepadButtonControl.LeftShoulder => GamepadButton.GAMEPAD_BUTTON_LEFT_TRIGGER_1,
            GamepadButtonControl.RightShoulder => GamepadButton.GAMEPAD_BUTTON_RIGHT_TRIGGER_1,
            GamepadButtonControl.LeftTrigger => GamepadButton.GAMEPAD_BUTTON_LEFT_TRIGGER_2,
            GamepadButtonControl.RightTrigger => GamepadButton.GAMEPAD_BUTTON_RIGHT_TRIGGER_2,
            GamepadButtonControl.MiddleLeft => GamepadButton.GAMEPAD_BUTTON_MIDDLE_LEFT,
            GamepadButtonControl.Middle => GamepadButton.GAMEPAD_BUTTON_MIDDLE,
            GamepadButtonControl.MiddleRight => GamepadButton.GAMEPAD_BUTTON_MIDDLE_RIGHT,
            GamepadButtonControl.LeftThumb => GamepadButton.GAMEPAD_BUTTON_LEFT_THUMB,
            GamepadButtonControl.RightThumb => GamepadButton.GAMEPAD_BUTTON_RIGHT_THUMB,
            _ => throw new InvalidOperationException(
                $"LUDOTS_INPUT_GAMEPAD_CONTROL_UNKNOWN: button control '{button}' has no raylib mapping."),
        };

        public static GamepadAxis ToRaylibAxis(GamepadAxisControl axis) => axis switch
        {
            GamepadAxisControl.LeftX => GamepadAxis.GAMEPAD_AXIS_LEFT_X,
            GamepadAxisControl.LeftY => GamepadAxis.GAMEPAD_AXIS_LEFT_Y,
            GamepadAxisControl.RightX => GamepadAxis.GAMEPAD_AXIS_RIGHT_X,
            GamepadAxisControl.RightY => GamepadAxis.GAMEPAD_AXIS_RIGHT_Y,
            GamepadAxisControl.LeftTrigger => GamepadAxis.GAMEPAD_AXIS_LEFT_TRIGGER,
            GamepadAxisControl.RightTrigger => GamepadAxis.GAMEPAD_AXIS_RIGHT_TRIGGER,
            _ => throw new InvalidOperationException(
                $"LUDOTS_INPUT_GAMEPAD_CONTROL_UNKNOWN: axis control '{axis}' has no raylib mapping."),
        };
    }
}
