using System;

namespace Ludots.Core.Map
{
    /// <summary>
    /// Represents a 256x256 block of grids.
    /// </summary>
    public class MapTile
    {
        public const int Size = 256;
        
        // Flattened arrays for better memory locality
        public readonly byte[] HeightLevels;
        public readonly bool[] Blocked;

        public MapTile()
        {
            HeightLevels = new byte[Size * Size];
            Blocked = new bool[Size * Size];
        }

        public byte GetHeight(int localX, int localY)
        {
            if (localX < 0 || localX >= Size || localY < 0 || localY >= Size) return 0;
            return HeightLevels[localY * Size + localX];
        }

        public void SetHeight(int localX, int localY, byte height)
        {
            if (localX >= 0 && localX < Size && localY >= 0 && localY < Size)
                HeightLevels[localY * Size + localX] = height;
        }

        public bool IsBlocked(int localX, int localY)
        {
             if (localX < 0 || localX >= Size || localY < 0 || localY >= Size) return true;
             return Blocked[localY * Size + localX];
        }

        public void SetBlocked(int localX, int localY, bool blocked)
        {
            if (localX >= 0 && localX < Size && localY >= 0 && localY < Size)
                Blocked[localY * Size + localX] = blocked;
        }
    }
}
