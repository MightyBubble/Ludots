using System;
using Silk.NET.Input;

namespace Ludots.Client.WebGpu.Input
{
    public static class WebGpuInputPathParser
    {
        public static Key? ParseKeyboardKey(string path)
        {
            if (!path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string keyName = path.Substring(11).ToUpperInvariant();
            if (keyName.Length == 1)
            {
                char c = keyName[0];
                if (c >= 'A' && c <= 'Z')
                {
                    return Key.A + (c - 'A');
                }

                if (c >= '0' && c <= '9')
                {
                    return Key.Number0 + (c - '0');
                }
            }

            if (keyName.Length >= 2 &&
                keyName[0] == 'F' &&
                int.TryParse(keyName.AsSpan(1), out int fNum) &&
                fNum >= 1 && fNum <= 12)
            {
                return Key.F1 + (fNum - 1);
            }

            return keyName switch
            {
                "SPACE" => Key.Space,
                "ENTER" => Key.Enter,
                "ESCAPE" => Key.Escape,
                "TAB" => Key.Tab,
                "BACKSPACE" => Key.Backspace,
                "INSERT" => Key.Insert,
                "DELETE" => Key.Delete,
                "PAGEUP" => Key.PageUp,
                "PAGEDOWN" => Key.PageDown,
                "HOME" => Key.Home,
                "END" => Key.End,
                "MINUS" => Key.Minus,
                "EQUAL" => Key.Equal,
                "EQUALS" => Key.Equal,
                "LEFT" => Key.Left,
                "RIGHT" => Key.Right,
                "UP" => Key.Up,
                "DOWN" => Key.Down,
                "LEFTSHIFT" => Key.ShiftLeft,
                "LEFTCONTROL" => Key.ControlLeft,
                "LEFTCTRL" => Key.ControlLeft,
                "LEFTALT" => Key.AltLeft,
                "RIGHTSHIFT" => Key.ShiftRight,
                "RIGHTCONTROL" => Key.ControlRight,
                "RIGHTCTRL" => Key.ControlRight,
                "RIGHTALT" => Key.AltRight,
                _ => null
            };
        }

        public static MouseButton? ParseMouseButton(string path)
        {
            if (!path.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string btnName = path.Substring(8).ToUpperInvariant();
            return btnName switch
            {
                "LEFTBUTTON" => MouseButton.Left,
                "RIGHTBUTTON" => MouseButton.Right,
                "MIDDLEBUTTON" => MouseButton.Middle,
                _ => null
            };
        }
    }
}
