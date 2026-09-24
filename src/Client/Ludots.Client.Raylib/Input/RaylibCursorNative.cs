using System.Numerics;
using System.Runtime.InteropServices;

namespace Ludots.Client.Raylib.Input
{
    public static class RaylibCursorNative
    {
        private const string NativeLib = "raylib";

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DisableCursor();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void EnableCursor();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void HideCursor();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ShowCursor();

        [DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
        public static extern Vector2 GetMouseDelta();
    }
}
