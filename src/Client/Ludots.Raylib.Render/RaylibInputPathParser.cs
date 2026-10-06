using System;
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
        /// Gamepad index from a "&lt;Gamepad&gt;" / "&lt;Gamepad3&gt;" style device tag.
        /// "&lt;Gamepad&gt;" addresses the primary pad (slot 0); an explicit digit
        /// addresses that slot directly ("&lt;Gamepad1&gt;" is also slot 0).
        /// Returns -1 when the tag is not a gamepad tag.
        /// </summary>
        public static int ParseGamepadIndex(string path)
        {
            if (!path.StartsWith("<Gamepad", StringComparison.OrdinalIgnoreCase)) return -1;
            int close = path.IndexOf('>');
            if (close < 0) return -1;

            string tag = path.Substring(1, close - 1);
            if (tag.Length > "Gamepad".Length &&
                int.TryParse(tag.AsSpan("Gamepad".Length), out int slot) &&
                slot is >= 1 and <= 3)
            {
                return slot - 1;
            }

            return 0;
        }

        /// <summary>
        /// Button control under "&lt;Gamepad&gt;/…". Names are layout-abstract
        /// (buttonSouth is A on Xbox, cross on PlayStation). Dpad accepts both
        /// "dpad/up" and "leftFaceUp" spellings; triggers appear here as digital
        /// buttons and under <see cref="ParseGamepadAxis"/> as analog axes.
        /// </summary>
        public static GamepadButton? ParseGamepadButton(string path)
        {
            if (ParseGamepadIndex(path) < 0) return null;
            int slash = path.IndexOf('/');
            if (slash < 0) return null;
            string control = path.Substring(slash + 1).Replace("/", "").ToUpperInvariant();

            return control switch
            {
                "BUTTONSOUTH" or "BUTTONBOTTOM" or "A" => GamepadButton.GAMEPAD_BUTTON_RIGHT_FACE_DOWN,
                "BUTTONEAST" or "BUTTONRIGHT" or "B" or "CANCEL" => GamepadButton.GAMEPAD_BUTTON_RIGHT_FACE_RIGHT,
                "BUTTONWEST" or "BUTTONLEFT" or "X" => GamepadButton.GAMEPAD_BUTTON_RIGHT_FACE_LEFT,
                "BUTTONNORTH" or "BUTTONTOP" or "Y" => GamepadButton.GAMEPAD_BUTTON_RIGHT_FACE_UP,
                "DPADUP" or "LEFTFACEUP" => GamepadButton.GAMEPAD_BUTTON_LEFT_FACE_UP,
                "DPADRIGHT" or "LEFTFACERIGHT" => GamepadButton.GAMEPAD_BUTTON_LEFT_FACE_RIGHT,
                "DPADDOWN" or "LEFTFACEDOWN" => GamepadButton.GAMEPAD_BUTTON_LEFT_FACE_DOWN,
                "DPADLEFT" or "LEFTFACELEFT" => GamepadButton.GAMEPAD_BUTTON_LEFT_FACE_LEFT,
                "LEFTSHOULDER" or "LB" or "LEFTBUMPER" => GamepadButton.GAMEPAD_BUTTON_LEFT_TRIGGER_1,
                "RIGHTSHOULDER" or "RB" or "RIGHTBUMPER" => GamepadButton.GAMEPAD_BUTTON_RIGHT_TRIGGER_1,
                "LEFTTRIGGER" or "LT" => GamepadButton.GAMEPAD_BUTTON_LEFT_TRIGGER_2,
                "RIGHTTRIGGER" or "RT" => GamepadButton.GAMEPAD_BUTTON_RIGHT_TRIGGER_2,
                "SELECT" or "BACK" or "MIDDLELEFT" => GamepadButton.GAMEPAD_BUTTON_MIDDLE_LEFT,
                "START" or "MENU" or "MIDDLE" => GamepadButton.GAMEPAD_BUTTON_MIDDLE,
                "MIDDLESHOULDER" or "GUIDE" or "MIDDLERIGHT" => GamepadButton.GAMEPAD_BUTTON_MIDDLE_RIGHT,
                "LEFTSTICKPRESS" or "L3" or "LEFTTHUMB" => GamepadButton.GAMEPAD_BUTTON_LEFT_THUMB,
                "RIGHTSTICKPRESS" or "R3" or "RIGHTTHUMB" => GamepadButton.GAMEPAD_BUTTON_RIGHT_THUMB,
                _ => null
            };
        }

        /// <summary>
        /// Single analog axis under "&lt;Gamepad&gt;/…": stick components
        /// ("leftStick/x", "leftStick/y", …) and triggers ("leftTrigger", no
        /// component suffix). Returns null for non-axis paths.
        /// </summary>
        public static (int Pad, GamepadAxis Axis)? ParseGamepadAxis(string path)
        {
            int pad = ParseGamepadIndex(path);
            if (pad < 0) return null;
            int slash = path.IndexOf('/');
            if (slash < 0) return null;
            string control = path.Substring(slash + 1).Replace("/", "").ToUpperInvariant();

            return control switch
            {
                "LEFTSTICKX" => (pad, GamepadAxis.GAMEPAD_AXIS_LEFT_X),
                "LEFTSTICKY" => (pad, GamepadAxis.GAMEPAD_AXIS_LEFT_Y),
                "RIGHTSTICKX" => (pad, GamepadAxis.GAMEPAD_AXIS_RIGHT_X),
                "RIGHTSTICKY" => (pad, GamepadAxis.GAMEPAD_AXIS_RIGHT_Y),
                "LEFTTRIGGER" or "LT" => (pad, GamepadAxis.GAMEPAD_AXIS_LEFT_TRIGGER),
                "RIGHTTRIGGER" or "RT" => (pad, GamepadAxis.GAMEPAD_AXIS_RIGHT_TRIGGER),
                _ => null
            };
        }
    }
}
