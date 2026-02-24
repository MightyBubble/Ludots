using System;
using Raylib_cs;

namespace Ludots.Client.Raylib.Input
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

            return keyName switch
            {
                "SPACE" => KeyboardKey.KEY_SPACE,
                "ENTER" => KeyboardKey.KEY_ENTER,
                "ESCAPE" => KeyboardKey.KEY_ESCAPE,
                "LEFT" => KeyboardKey.KEY_LEFT,
                "RIGHT" => KeyboardKey.KEY_RIGHT,
                "UP" => KeyboardKey.KEY_UP,
                "DOWN" => KeyboardKey.KEY_DOWN,
                "LEFTSHIFT" => KeyboardKey.KEY_LEFT_SHIFT,
                "LEFTCONTROL" => KeyboardKey.KEY_LEFT_CONTROL,
                "LEFTALT" => KeyboardKey.KEY_LEFT_ALT,
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
    }
}
