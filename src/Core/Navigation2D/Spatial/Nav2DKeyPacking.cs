using System.Runtime.CompilerServices;

namespace Ludots.Core.Navigation2D.Spatial
{
    public static class Nav2DKeyPacking
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long PackInt2(int x, int y) => ((long)x & 0xffffffff) | ((long)y << 32);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnpackInt2(long key, out int x, out int y)
        {
            x = (int)(key & 0xffffffff);
            y = (int)(key >> 32);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            int r = value % divisor;
            if ((r != 0) && ((r ^ divisor) < 0)) q--;
            return q;
        }
    }
}
