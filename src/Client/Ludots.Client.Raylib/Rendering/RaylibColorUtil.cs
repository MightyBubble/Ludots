using System.Numerics;
using System.Runtime.CompilerServices;
using Raylib_cs;

namespace Ludots.Client.Raylib.Rendering
{
    public static class RaylibColorUtil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color ToRaylibColor(in Vector4 c)
        {
            byte r = Clamp01ToByte(c.X);
            byte g = Clamp01ToByte(c.Y);
            byte b = Clamp01ToByte(c.Z);
            byte a = Clamp01ToByte(c.W);
            return new Color(r, g, b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte Clamp01ToByte(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f);
        }
    }
}
