using System;
using System.Text;
using Ludots.UI;
using Ludots.UI.Input;

namespace Ludots.Adapter.Raylib;

internal static class RaylibSyntheticKeyboardInput
{
    public static bool SendKeyStroke(UIRoot uiRoot, string key, string? code = null, int modifiers = 0)
    {
        ArgumentNullException.ThrowIfNull(uiRoot);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        string normalizedCode = string.IsNullOrWhiteSpace(code) ? key : code;
        bool downHandled = uiRoot.HandleInput(new KeyboardEvent
        {
            DeviceType = InputDeviceType.Keyboard,
            Action = KeyboardAction.Down,
            Key = key,
            Code = normalizedCode,
            Modifiers = modifiers
        });
        bool upHandled = uiRoot.HandleInput(new KeyboardEvent
        {
            DeviceType = InputDeviceType.Keyboard,
            Action = KeyboardAction.Up,
            Key = key,
            Code = normalizedCode,
            Modifiers = modifiers
        });

        return downHandled || upHandled;
    }

    public static bool SendTextInput(UIRoot uiRoot, string text, int modifiers = 0)
    {
        ArgumentNullException.ThrowIfNull(uiRoot);
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        bool handled = false;
        for (int offset = 0; offset < text.Length;)
        {
            if (!Rune.TryGetRuneAt(text, offset, out Rune rune))
            {
                break;
            }

            string character = rune.ToString();
            handled |= uiRoot.HandleInput(new KeyboardEvent
            {
                DeviceType = InputDeviceType.Keyboard,
                Action = KeyboardAction.Character,
                Key = character,
                Text = character,
                Modifiers = modifiers
            });
            offset += rune.Utf16SequenceLength;
        }

        return handled;
    }
}
