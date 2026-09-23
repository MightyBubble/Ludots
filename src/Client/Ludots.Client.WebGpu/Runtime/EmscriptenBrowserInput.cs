using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ludots.Client.WebGpu.Native;

namespace Ludots.Client.WebGpu.Runtime;

public enum EmscriptenInputKey : byte
{
    A = 0,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
    Digit0,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Digit5,
    Digit6,
    Digit7,
    Digit8,
    Digit9,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    Space,
    Enter,
    Escape,
    Tab,
    Backspace,
    Insert,
    Delete,
    PageUp,
    PageDown,
    Home,
    End,
    Minus,
    Equal,
    Left,
    Right,
    Up,
    Down,
    LeftShift,
    LeftControl,
    LeftAlt,
    RightShift,
    RightControl,
    RightAlt,
}

public enum EmscriptenMouseButton : byte
{
    Left = 0,
    Right = 1,
    Middle = 2,
}

public sealed unsafe class EmscriptenBrowserInput
{
    private const int TransitionCapacity = 128;
    private const int TransitionMask = TransitionCapacity - 1;
    private const float WheelPixelScale = -0.01f;

    private static EmscriptenBrowserInput? s_active;

    private readonly InputState[] _transitions = new InputState[TransitionCapacity];
    private InputState _pending;
    private InputState _frame;
    private int _transitionRead;
    private int _transitionCount;
    private float _pendingWheel;
    private float _frameWheel;
    private InputFault _fault;
    private bool _frameInitialized;

    public EmscriptenBrowserInput()
    {
        if (!OperatingSystem.IsBrowser())
        {
            throw new PlatformNotSupportedException(
                "Emscripten browser input can only be registered by a browser-wasm runtime.");
        }

        if (s_active != null)
        {
            throw new InvalidOperationException(
                "Only one Emscripten browser input source can own the document and canvas callbacks.");
        }

        ValidateAbiLayouts();
        s_active = this;
        try
        {
            RegisterCallbacks();
        }
        catch
        {
            s_active = null;
            throw;
        }
    }

    public void AdvanceFrame()
    {
        if (_fault == InputFault.TransitionOverflow)
        {
            throw new InvalidOperationException(
                $"Emscripten input transition capacity ({TransitionCapacity}) was exceeded before a frame consumed the events.");
        }

        if (_fault == InputFault.NonFiniteWheel)
        {
            throw new InvalidOperationException("Emscripten delivered a non-finite wheel delta.");
        }

        if (_transitionCount > 0)
        {
            _frame = _transitions[_transitionRead];
            _transitionRead = (_transitionRead + 1) & TransitionMask;
            _transitionCount--;
        }
        else
        {
            _frame = _pending;
        }

        _frameWheel = _pendingWheel;
        _pendingWheel = 0f;
        _frameInitialized = true;
    }

    public bool IsKeyDown(EmscriptenInputKey key)
    {
        ref readonly InputState state = ref CurrentState;
        int index = (int)key;
        return index < 64
            ? (state.KeyBitsLow & (1UL << index)) != 0
            : (state.KeyBitsHigh & (1UL << (index - 64))) != 0;
    }

    public bool IsMouseButtonDown(EmscriptenMouseButton button)
    {
        ref readonly InputState state = ref CurrentState;
        int mask = button switch
        {
            EmscriptenMouseButton.Left => 1,
            EmscriptenMouseButton.Right => 2,
            EmscriptenMouseButton.Middle => 4,
            _ => 0,
        };
        return (state.MouseButtons & mask) != 0;
    }

    public Vector2 GetMousePosition()
    {
        ref readonly InputState state = ref CurrentState;
        return state.PointerValid
            ? new Vector2(state.MouseX, state.MouseY)
            : new Vector2(-1f, -1f);
    }

    public float GetMouseWheel() => _frameInitialized ? _frameWheel : _pendingWheel;

    private ref readonly InputState CurrentState =>
        ref _frameInitialized ? ref _frame : ref _pending;

    private static void ValidateAbiLayouts()
    {
        if (sizeof(EmscriptenKeyboardEvent) != 176 ||
            sizeof(EmscriptenMouseEvent) != 72 ||
            sizeof(EmscriptenWheelEvent) != 104 ||
            sizeof(EmscriptenFocusEvent) != 256)
        {
            throw new InvalidOperationException(
                $"Emscripten HTML5 event ABI mismatch: keyboard={sizeof(EmscriptenKeyboardEvent)}, " +
                $"mouse={sizeof(EmscriptenMouseEvent)}, wheel={sizeof(EmscriptenWheelEvent)}, " +
                $"focus={sizeof(EmscriptenFocusEvent)}.");
        }
    }

    private static void RegisterCallbacks()
    {
        byte* documentTarget = (byte*)1;
        byte* windowTarget = (byte*)2;
        ReadOnlySpan<byte> canvasSelector = "#canvas\0"u8;
        fixed (byte* canvasTarget = canvasSelector)
        {
            RequireRegistration(
                "keydown(document)",
                EmscriptenNative.SetKeyDownCallbackOnThread(documentTarget, null, &OnKeyDown));
            RequireRegistration(
                "keyup(document)",
                EmscriptenNative.SetKeyUpCallbackOnThread(documentTarget, null, &OnKeyUp));
            RequireRegistration(
                "mousedown(#canvas)",
                EmscriptenNative.SetMouseDownCallbackOnThread(canvasTarget, null, &OnMouseDown));
            RequireRegistration(
                "mouseup(#canvas)",
                EmscriptenNative.SetMouseUpCallbackOnThread(canvasTarget, null, &OnMouseUp));
            RequireRegistration(
                "mousemove(#canvas)",
                EmscriptenNative.SetMouseMoveCallbackOnThread(canvasTarget, null, &OnMouseMove));
            RequireRegistration(
                "wheel(#canvas)",
                EmscriptenNative.SetWheelCallbackOnThread(canvasTarget, null, &OnWheel));
            RequireRegistration(
                "blur(window)",
                EmscriptenNative.SetBlurCallbackOnThread(windowTarget, null, &OnBlur));
        }
    }

    private static void RequireRegistration(string eventName, int result)
    {
        if (result != 0)
        {
            throw new InvalidOperationException(
                $"emscripten callback registration failed for {eventName} with result {result}.");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnKeyDown(int eventType, EmscriptenKeyboardEvent* keyboardEvent, void* userData)
    {
        EmscriptenBrowserInput? input = s_active;
        return input != null && keyboardEvent != null && input.ApplyKey(keyboardEvent, isDown: true)
            ? 1
            : 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnKeyUp(int eventType, EmscriptenKeyboardEvent* keyboardEvent, void* userData)
    {
        EmscriptenBrowserInput? input = s_active;
        return input != null && keyboardEvent != null && input.ApplyKey(keyboardEvent, isDown: false)
            ? 1
            : 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnMouseDown(int eventType, EmscriptenMouseEvent* mouseEvent, void* userData)
    {
        EmscriptenBrowserInput? input = s_active;
        if (input == null || mouseEvent == null)
        {
            return 0;
        }

        input.ApplyMouse(mouseEvent);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnMouseUp(int eventType, EmscriptenMouseEvent* mouseEvent, void* userData)
    {
        EmscriptenBrowserInput? input = s_active;
        if (input == null || mouseEvent == null)
        {
            return 0;
        }

        input.ApplyMouse(mouseEvent);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnMouseMove(int eventType, EmscriptenMouseEvent* mouseEvent, void* userData)
    {
        EmscriptenBrowserInput? input = s_active;
        if (input == null || mouseEvent == null)
        {
            return 0;
        }

        input.ApplyMouse(mouseEvent);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnWheel(int eventType, EmscriptenWheelEvent* wheelEvent, void* userData)
    {
        EmscriptenBrowserInput? input = s_active;
        if (input == null || wheelEvent == null)
        {
            return 0;
        }

        input.ApplyMouse(&wheelEvent->Mouse);
        double deltaY = wheelEvent->DeltaY;
        if (!double.IsFinite(deltaY))
        {
            input._fault = InputFault.NonFiniteWheel;
            return 1;
        }

        input._pendingWheel += (float)deltaY * WheelPixelScale;
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnBlur(int eventType, EmscriptenFocusEvent* focusEvent, void* userData)
    {
        EmscriptenBrowserInput? input = s_active;
        if (input == null)
        {
            return 0;
        }

        bool changed = input._pending.KeyBitsLow != 0 ||
            input._pending.KeyBitsHigh != 0 ||
            input._pending.MouseButtons != 0;
        input._pending.KeyBitsLow = 0;
        input._pending.KeyBitsHigh = 0;
        input._pending.MouseButtons = 0;
        input._pending.PointerValid = false;
        if (changed)
        {
            input.EnqueueTransition();
        }

        return 0;
    }

    private bool ApplyKey(EmscriptenKeyboardEvent* keyboardEvent, bool isDown)
    {
        if (!TryResolveKey(keyboardEvent->Code, out EmscriptenInputKey key))
        {
            return false;
        }

        int index = (int)key;
        ulong mask = 1UL << (index & 63);
        bool changed;
        if (index < 64)
        {
            bool wasDown = (_pending.KeyBitsLow & mask) != 0;
            changed = wasDown != isDown;
            _pending.KeyBitsLow = isDown
                ? _pending.KeyBitsLow | mask
                : _pending.KeyBitsLow & ~mask;
        }
        else
        {
            bool wasDown = (_pending.KeyBitsHigh & mask) != 0;
            changed = wasDown != isDown;
            _pending.KeyBitsHigh = isDown
                ? _pending.KeyBitsHigh | mask
                : _pending.KeyBitsHigh & ~mask;
        }

        if (changed)
        {
            EnqueueTransition();
        }

        return true;
    }

    private void ApplyMouse(EmscriptenMouseEvent* mouseEvent)
    {
        _pending.MouseX = mouseEvent->TargetX;
        _pending.MouseY = mouseEvent->TargetY;
        _pending.PointerValid = true;

        byte buttons = (byte)(mouseEvent->Buttons & 0b111);
        if (_pending.MouseButtons != buttons)
        {
            _pending.MouseButtons = buttons;
            EnqueueTransition();
        }
    }

    private void EnqueueTransition()
    {
        if (_transitionCount == TransitionCapacity)
        {
            _fault = InputFault.TransitionOverflow;
            return;
        }

        int writeIndex = (_transitionRead + _transitionCount) & TransitionMask;
        _transitions[writeIndex] = _pending;
        _transitionCount++;
    }

    private static bool TryResolveKey(byte* code, out EmscriptenInputKey key)
    {
        if (code[0] == (byte)'K' && code[1] == (byte)'e' && code[2] == (byte)'y' &&
            code[3] >= (byte)'A' && code[3] <= (byte)'Z' && code[4] == 0)
        {
            key = (EmscriptenInputKey)(code[3] - (byte)'A');
            return true;
        }

        if (code[0] == (byte)'D' && code[1] == (byte)'i' && code[2] == (byte)'g' &&
            code[3] == (byte)'i' && code[4] == (byte)'t' &&
            code[5] >= (byte)'0' && code[5] <= (byte)'9' && code[6] == 0)
        {
            key = (EmscriptenInputKey)((int)EmscriptenInputKey.Digit0 + (code[5] - (byte)'0'));
            return true;
        }

        if (code[0] == (byte)'F' && code[1] >= (byte)'1' && code[1] <= (byte)'9')
        {
            int functionNumber = code[1] - (byte)'0';
            if (code[2] != 0)
            {
                if (code[1] != (byte)'1' || code[2] < (byte)'0' || code[2] > (byte)'2' || code[3] != 0)
                {
                    key = default;
                    return false;
                }

                functionNumber = 10 + (code[2] - (byte)'0');
            }

            key = (EmscriptenInputKey)((int)EmscriptenInputKey.F1 + (functionNumber - 1));
            return true;
        }

        if (AsciiEquals(code, "Space"u8)) key = EmscriptenInputKey.Space;
        else if (AsciiEquals(code, "Enter"u8) || AsciiEquals(code, "NumpadEnter"u8)) key = EmscriptenInputKey.Enter;
        else if (AsciiEquals(code, "Escape"u8)) key = EmscriptenInputKey.Escape;
        else if (AsciiEquals(code, "Tab"u8)) key = EmscriptenInputKey.Tab;
        else if (AsciiEquals(code, "Backspace"u8)) key = EmscriptenInputKey.Backspace;
        else if (AsciiEquals(code, "Insert"u8)) key = EmscriptenInputKey.Insert;
        else if (AsciiEquals(code, "Delete"u8)) key = EmscriptenInputKey.Delete;
        else if (AsciiEquals(code, "PageUp"u8)) key = EmscriptenInputKey.PageUp;
        else if (AsciiEquals(code, "PageDown"u8)) key = EmscriptenInputKey.PageDown;
        else if (AsciiEquals(code, "Home"u8)) key = EmscriptenInputKey.Home;
        else if (AsciiEquals(code, "End"u8)) key = EmscriptenInputKey.End;
        else if (AsciiEquals(code, "Minus"u8) || AsciiEquals(code, "NumpadSubtract"u8)) key = EmscriptenInputKey.Minus;
        else if (AsciiEquals(code, "Equal"u8) || AsciiEquals(code, "NumpadAdd"u8)) key = EmscriptenInputKey.Equal;
        else if (AsciiEquals(code, "ArrowLeft"u8)) key = EmscriptenInputKey.Left;
        else if (AsciiEquals(code, "ArrowRight"u8)) key = EmscriptenInputKey.Right;
        else if (AsciiEquals(code, "ArrowUp"u8)) key = EmscriptenInputKey.Up;
        else if (AsciiEquals(code, "ArrowDown"u8)) key = EmscriptenInputKey.Down;
        else if (AsciiEquals(code, "ShiftLeft"u8)) key = EmscriptenInputKey.LeftShift;
        else if (AsciiEquals(code, "ControlLeft"u8)) key = EmscriptenInputKey.LeftControl;
        else if (AsciiEquals(code, "AltLeft"u8)) key = EmscriptenInputKey.LeftAlt;
        else if (AsciiEquals(code, "ShiftRight"u8)) key = EmscriptenInputKey.RightShift;
        else if (AsciiEquals(code, "ControlRight"u8)) key = EmscriptenInputKey.RightControl;
        else if (AsciiEquals(code, "AltRight"u8)) key = EmscriptenInputKey.RightAlt;
        else
        {
            key = default;
            return false;
        }

        return true;
    }

    private static bool AsciiEquals(byte* value, ReadOnlySpan<byte> expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            if (value[i] != expected[i])
            {
                return false;
            }
        }

        return value[expected.Length] == 0;
    }

    private struct InputState
    {
        internal ulong KeyBitsLow;
        internal ulong KeyBitsHigh;
        internal float MouseX;
        internal float MouseY;
        internal byte MouseButtons;
        internal bool PointerValid;
    }

    private enum InputFault : byte
    {
        None = 0,
        TransitionOverflow = 1,
        NonFiniteWheel = 2,
    }
}
