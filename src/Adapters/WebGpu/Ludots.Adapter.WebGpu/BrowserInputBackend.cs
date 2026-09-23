using System.Numerics;
using Ludots.Client.WebGpu.Runtime;
using Ludots.Core.Input.Runtime;

namespace Ludots.Adapter.WebGpu;

public sealed class BrowserInputBackend : IInputBackend, IFrameSynchronizedInputBackend
{
    private readonly EmscriptenBrowserInput _input = new();
    private bool _imeEnabled;

    public float GetAxis(string devicePath)
    {
        return devicePath.AsSpan().Equals("<Mouse>/ScrollY", StringComparison.OrdinalIgnoreCase)
            ? _input.GetMouseWheel()
            : 0f;
    }

    public bool GetButton(string devicePath)
    {
        ReadOnlySpan<char> path = devicePath.AsSpan();
        const string keyboardPrefix = "<Keyboard>/";
        if (path.StartsWith(keyboardPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return !_imeEnabled &&
                TryResolveKeyPath(path[keyboardPrefix.Length..], out EmscriptenInputKey key) &&
                _input.IsKeyDown(key);
        }

        const string mousePrefix = "<Mouse>/";
        if (!path.StartsWith(mousePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> button = path[mousePrefix.Length..];
        if (button.Equals("LeftButton", StringComparison.OrdinalIgnoreCase))
        {
            return _input.IsMouseButtonDown(EmscriptenMouseButton.Left);
        }

        if (button.Equals("RightButton", StringComparison.OrdinalIgnoreCase))
        {
            return _input.IsMouseButtonDown(EmscriptenMouseButton.Right);
        }

        return button.Equals("MiddleButton", StringComparison.OrdinalIgnoreCase) &&
            _input.IsMouseButtonDown(EmscriptenMouseButton.Middle);
    }

    public Vector2 GetMousePosition() => _input.GetMousePosition();

    public float GetMouseWheel() => _input.GetMouseWheel();

    public void AdvanceFrameInput() => _input.AdvanceFrame();

    public void EnableIME(bool enable)
    {
        _imeEnabled = enable;
    }

    public void SetIMECandidatePosition(int x, int y)
    {
    }

    public string GetCharBuffer() => string.Empty;

    private static bool TryResolveKeyPath(ReadOnlySpan<char> name, out EmscriptenInputKey key)
    {
        if (name.Length == 1)
        {
            char character = name[0];
            if (character >= 'a' && character <= 'z')
            {
                key = (EmscriptenInputKey)((int)EmscriptenInputKey.A + (character - 'a'));
                return true;
            }

            if (character >= 'A' && character <= 'Z')
            {
                key = (EmscriptenInputKey)((int)EmscriptenInputKey.A + (character - 'A'));
                return true;
            }

            if (character >= '0' && character <= '9')
            {
                key = (EmscriptenInputKey)((int)EmscriptenInputKey.Digit0 + (character - '0'));
                return true;
            }
        }

        if (name.Length >= 2 && name.Length <= 3 && (name[0] == 'f' || name[0] == 'F'))
        {
            int functionNumber = name[1] - '0';
            if (functionNumber >= 1 && functionNumber <= 9 && name.Length == 2)
            {
                key = (EmscriptenInputKey)((int)EmscriptenInputKey.F1 + (functionNumber - 1));
                return true;
            }

            if (name.Length == 3 && name[1] == '1' && name[2] >= '0' && name[2] <= '2')
            {
                functionNumber = 10 + (name[2] - '0');
                key = (EmscriptenInputKey)((int)EmscriptenInputKey.F1 + (functionNumber - 1));
                return true;
            }
        }

        if (name.Equals("space", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Space;
        else if (name.Equals("enter", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Enter;
        else if (name.Equals("escape", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Escape;
        else if (name.Equals("tab", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Tab;
        else if (name.Equals("backspace", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Backspace;
        else if (name.Equals("insert", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Insert;
        else if (name.Equals("delete", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Delete;
        else if (name.Equals("pageUp", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.PageUp;
        else if (name.Equals("pageDown", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.PageDown;
        else if (name.Equals("home", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Home;
        else if (name.Equals("end", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.End;
        else if (name.Equals("minus", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Minus;
        else if (name.Equals("equal", StringComparison.OrdinalIgnoreCase) || name.Equals("equals", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Equal;
        else if (name.Equals("left", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Left;
        else if (name.Equals("right", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Right;
        else if (name.Equals("up", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Up;
        else if (name.Equals("down", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.Down;
        else if (name.Equals("leftShift", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.LeftShift;
        else if (name.Equals("leftControl", StringComparison.OrdinalIgnoreCase) || name.Equals("leftCtrl", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.LeftControl;
        else if (name.Equals("leftAlt", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.LeftAlt;
        else if (name.Equals("rightShift", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.RightShift;
        else if (name.Equals("rightControl", StringComparison.OrdinalIgnoreCase) || name.Equals("rightCtrl", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.RightControl;
        else if (name.Equals("rightAlt", StringComparison.OrdinalIgnoreCase)) key = EmscriptenInputKey.RightAlt;
        else
        {
            key = default;
            return false;
        }

        return true;
    }
}
