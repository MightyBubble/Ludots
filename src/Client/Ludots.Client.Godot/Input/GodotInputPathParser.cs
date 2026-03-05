using System;

namespace Ludots.Client.Godot.Input
{
    /// <summary>
    /// Maps Ludots devicePath (e.g. "&lt;Keyboard>/w", "&lt;Mouse>/LeftButton") to Godot Key and MouseButton.
    /// </summary>
    public static class GodotInputPathParser
    {
        public static global::Godot.Key? ParseKeyboardKey(string path)
        {
            if (!path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase)) return null;

            string keyName = path.Substring(11).ToUpper();

            if (keyName.Length == 1)
            {
                char c = keyName[0];
                if (c >= 'A' && c <= 'Z') return (global::Godot.Key)((int)global::Godot.Key.A + (c - 'A'));
                if (c >= '0' && c <= '9') return (global::Godot.Key)((int)global::Godot.Key.Key0 + (c - '0'));
            }

            if (keyName.Length >= 2 &&
                keyName[0] == 'F' &&
                int.TryParse(keyName.AsSpan(1), out int fNum) &&
                fNum >= 1 && fNum <= 12)
            {
                return (global::Godot.Key)((int)global::Godot.Key.F1 + (fNum - 1));
            }

            return keyName switch
            {
                "SPACE" => global::Godot.Key.Space,
                "ENTER" or "RETURN" => global::Godot.Key.Enter,
                "ENTERORRETURN" => global::Godot.Key.Enter,
                "ESCAPE" => global::Godot.Key.Escape,
                "TAB" => global::Godot.Key.Tab,
                "BACKSPACE" => global::Godot.Key.Backspace,
                "DELETE" => global::Godot.Key.Delete,
                "LEFT" => global::Godot.Key.Left,
                "RIGHT" => global::Godot.Key.Right,
                "UP" => global::Godot.Key.Up,
                "DOWN" => global::Godot.Key.Down,
                "LEFTSHIFT" or "SHIFT" => global::Godot.Key.Shift,
                "LEFTCONTROL" or "CONTROL" => global::Godot.Key.Ctrl,
                "LEFTALT" or "ALT" => global::Godot.Key.Alt,
                _ => null
            };
        }

        public static global::Godot.MouseButton? ParseMouseButton(string path)
        {
            if (!path.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase)) return null;
            string btnName = path.Substring(8).ToUpper();

            return btnName switch
            {
                "LEFTBUTTON" or "LEFT" => global::Godot.MouseButton.Left,
                "RIGHTBUTTON" or "RIGHT" => global::Godot.MouseButton.Right,
                "MIDDLEBUTTON" or "MIDDLE" => global::Godot.MouseButton.Middle,
                "WHEELUP" => global::Godot.MouseButton.WheelUp,
                "WHEELDOWN" => global::Godot.MouseButton.WheelDown,
                _ => null
            };
        }
    }
}
