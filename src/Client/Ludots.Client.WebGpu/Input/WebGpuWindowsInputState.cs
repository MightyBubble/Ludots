using System;
using System.Runtime.InteropServices;
using Silk.NET.Input;

namespace Ludots.Client.WebGpu.Input
{
    public static class WebGpuWindowsInputMap
    {
        public static bool TryMapKeyboardVirtualKey(Key key, out int virtualKey)
        {
            if (key >= Key.A && key <= Key.Z)
            {
                virtualKey = 0x41 + (key - Key.A);
                return true;
            }

            if (key >= Key.Number0 && key <= Key.Number9)
            {
                virtualKey = 0x30 + (key - Key.Number0);
                return true;
            }

            if (key >= Key.F1 && key <= Key.F12)
            {
                virtualKey = 0x70 + (key - Key.F1);
                return true;
            }

            virtualKey = key switch
            {
                Key.Backspace => 0x08,
                Key.Tab => 0x09,
                Key.Enter => 0x0D,
                Key.ShiftLeft => 0xA0,
                Key.ShiftRight => 0xA1,
                Key.ControlLeft => 0xA2,
                Key.ControlRight => 0xA3,
                Key.AltLeft => 0xA4,
                Key.AltRight => 0xA5,
                Key.Escape => 0x1B,
                Key.Space => 0x20,
                Key.PageUp => 0x21,
                Key.PageDown => 0x22,
                Key.End => 0x23,
                Key.Home => 0x24,
                Key.Left => 0x25,
                Key.Up => 0x26,
                Key.Right => 0x27,
                Key.Down => 0x28,
                Key.Insert => 0x2D,
                Key.Delete => 0x2E,
                Key.Minus => 0xBD,
                Key.Equal => 0xBB,
                _ => 0
            };
            return virtualKey != 0;
        }

        public static bool TryMapMouseVirtualKey(MouseButton button, out int virtualKey)
        {
            virtualKey = button switch
            {
                MouseButton.Left => 0x01,
                MouseButton.Right => 0x02,
                MouseButton.Middle => 0x04,
                _ => 0
            };
            return virtualKey != 0;
        }
    }

    internal static class WebGpuWindowsInputState
    {
        public static bool TryGetKeyDown(Key key, out bool isDown)
        {
            if (!OperatingSystem.IsWindows() ||
                !WebGpuWindowsInputMap.TryMapKeyboardVirtualKey(key, out int virtualKey))
            {
                isDown = false;
                return false;
            }

            isDown = IsVirtualKeyDown(virtualKey);
            return true;
        }

        public static bool TryGetMouseButtonDown(MouseButton button, out bool isDown)
        {
            if (!OperatingSystem.IsWindows() ||
                !WebGpuWindowsInputMap.TryMapMouseVirtualKey(button, out int virtualKey))
            {
                isDown = false;
                return false;
            }

            isDown = IsVirtualKeyDown(virtualKey);
            return true;
        }

        private static bool IsVirtualKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
