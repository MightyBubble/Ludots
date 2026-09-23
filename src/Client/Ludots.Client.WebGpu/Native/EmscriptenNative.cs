using System.Runtime.InteropServices;

namespace Ludots.Client.WebGpu.Native;

internal static unsafe class EmscriptenNative
{
    private const string Module = "__Internal_emscripten";
    private const nint CallbackThreadCallingThread = 0x2;

    [DllImport(Module, EntryPoint = "emscripten_set_main_loop", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetMainLoopNative(nint callback, int framesPerSecond, int simulateInfiniteLoop);

    [DllImport(Module, EntryPoint = "emscripten_get_element_css_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetElementCssSize(byte* selector, double* width, double* height);

    [DllImport(Module, EntryPoint = "emscripten_set_canvas_element_size", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SetCanvasElementSize(byte* selector, int width, int height);

    [DllImport(Module, EntryPoint = "emscripten_set_keydown_callback_on_thread", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SetKeyDownCallbackOnThreadNative(
        byte* target,
        void* userData,
        int useCapture,
        nint callback,
        nint targetThread);

    [DllImport(Module, EntryPoint = "emscripten_set_keyup_callback_on_thread", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SetKeyUpCallbackOnThreadNative(
        byte* target,
        void* userData,
        int useCapture,
        nint callback,
        nint targetThread);

    [DllImport(Module, EntryPoint = "emscripten_set_mousedown_callback_on_thread", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SetMouseDownCallbackOnThreadNative(
        byte* target,
        void* userData,
        int useCapture,
        nint callback,
        nint targetThread);

    [DllImport(Module, EntryPoint = "emscripten_set_mouseup_callback_on_thread", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SetMouseUpCallbackOnThreadNative(
        byte* target,
        void* userData,
        int useCapture,
        nint callback,
        nint targetThread);

    [DllImport(Module, EntryPoint = "emscripten_set_mousemove_callback_on_thread", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SetMouseMoveCallbackOnThreadNative(
        byte* target,
        void* userData,
        int useCapture,
        nint callback,
        nint targetThread);

    [DllImport(Module, EntryPoint = "emscripten_set_wheel_callback_on_thread", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SetWheelCallbackOnThreadNative(
        byte* target,
        void* userData,
        int useCapture,
        nint callback,
        nint targetThread);

    [DllImport(Module, EntryPoint = "emscripten_set_blur_callback_on_thread", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SetBlurCallbackOnThreadNative(
        byte* target,
        void* userData,
        int useCapture,
        nint callback,
        nint targetThread);

    internal static void SetMainLoop(delegate* unmanaged[Cdecl]<void> callback)
    {
        SetMainLoopNative((nint)callback, 0, 0);
    }

    internal static int SetKeyDownCallbackOnThread(
        byte* target,
        void* userData,
        delegate* unmanaged[Cdecl]<int, EmscriptenKeyboardEvent*, void*, int> callback)
    {
        return SetKeyDownCallbackOnThreadNative(
            target,
            userData,
            0,
            (nint)callback,
            CallbackThreadCallingThread);
    }

    internal static int SetKeyUpCallbackOnThread(
        byte* target,
        void* userData,
        delegate* unmanaged[Cdecl]<int, EmscriptenKeyboardEvent*, void*, int> callback)
    {
        return SetKeyUpCallbackOnThreadNative(
            target,
            userData,
            0,
            (nint)callback,
            CallbackThreadCallingThread);
    }

    internal static int SetMouseDownCallbackOnThread(
        byte* target,
        void* userData,
        delegate* unmanaged[Cdecl]<int, EmscriptenMouseEvent*, void*, int> callback)
    {
        return SetMouseDownCallbackOnThreadNative(
            target,
            userData,
            0,
            (nint)callback,
            CallbackThreadCallingThread);
    }

    internal static int SetMouseUpCallbackOnThread(
        byte* target,
        void* userData,
        delegate* unmanaged[Cdecl]<int, EmscriptenMouseEvent*, void*, int> callback)
    {
        return SetMouseUpCallbackOnThreadNative(
            target,
            userData,
            0,
            (nint)callback,
            CallbackThreadCallingThread);
    }

    internal static int SetMouseMoveCallbackOnThread(
        byte* target,
        void* userData,
        delegate* unmanaged[Cdecl]<int, EmscriptenMouseEvent*, void*, int> callback)
    {
        return SetMouseMoveCallbackOnThreadNative(
            target,
            userData,
            0,
            (nint)callback,
            CallbackThreadCallingThread);
    }

    internal static int SetWheelCallbackOnThread(
        byte* target,
        void* userData,
        delegate* unmanaged[Cdecl]<int, EmscriptenWheelEvent*, void*, int> callback)
    {
        return SetWheelCallbackOnThreadNative(
            target,
            userData,
            0,
            (nint)callback,
            CallbackThreadCallingThread);
    }

    internal static int SetBlurCallbackOnThread(
        byte* target,
        void* userData,
        delegate* unmanaged[Cdecl]<int, EmscriptenFocusEvent*, void*, int> callback)
    {
        return SetBlurCallbackOnThreadNative(
            target,
            userData,
            0,
            (nint)callback,
            CallbackThreadCallingThread);
    }
}
