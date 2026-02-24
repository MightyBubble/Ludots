using Ludots.Core.Mathematics;

namespace Ludots.Core.Navigation.GraphWorld
{
    public static class GraphChunkKey
    {
        public static long Pack(int chunkX, int chunkY)
        {
            return (long)chunkX & 0xFFFFFFFF | ((long)chunkY << 32);
        }

        public static (int x, int y) Unpack(long key)
        {
            int x = (int)(key & 0xFFFFFFFF);
            int y = (int)(key >> 32);
            return (x, y);
        }

        public static long FromWorld(WorldCmInt2 world, int chunkSizeCm)
        {
            int cx = MathUtil.FloorDiv(world.X, chunkSizeCm);
            int cy = MathUtil.FloorDiv(world.Y, chunkSizeCm);
            return Pack(cx, cy);
        }
    }
}

