using System;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Fields
{
    public readonly struct FieldGridSpec2D
    {
        public FieldGridSpec2D(int cellSizeCm, int chunkSizeCells)
        {
            if (cellSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            }

            if (chunkSizeCells <= 0 || (chunkSizeCells & (chunkSizeCells - 1)) != 0)
            {
                throw new ArgumentException("Field chunk size must be a positive power of two.", nameof(chunkSizeCells));
            }

            CellSizeCm = cellSizeCm;
            ChunkSizeCells = chunkSizeCells;
            ChunkMask = chunkSizeCells - 1;
            ChunkShift = CalculateShift(chunkSizeCells);
        }

        public readonly int CellSizeCm;
        public readonly int ChunkSizeCells;
        public readonly int ChunkMask;
        public readonly int ChunkShift;

        public FieldCell2D WorldToCell(WorldCmInt2 world)
        {
            return new FieldCell2D(
                MathUtil.FloorDiv(world.X, CellSizeCm),
                MathUtil.FloorDiv(world.Y, CellSizeCm));
        }

        public WorldCmInt2 CellCenterToWorld(FieldCell2D cell)
        {
            int half = CellSizeCm / 2;
            return new WorldCmInt2(
                (cell.X * CellSizeCm) + half,
                (cell.Y * CellSizeCm) + half);
        }

        public int ChunkCoord(int cellCoord) => MathUtil.FloorDiv(cellCoord, ChunkSizeCells);

        public int LocalIndex(int cellX, int cellY)
        {
            int chunkX = ChunkCoord(cellX);
            int chunkY = ChunkCoord(cellY);
            int localX = cellX - (chunkX * ChunkSizeCells);
            int localY = cellY - (chunkY * ChunkSizeCells);
            return (localY * ChunkSizeCells) + localX;
        }

        public FieldCell2D CellFromChunkLocal(int chunkX, int chunkY, int localIndex)
        {
            int localX = localIndex & ChunkMask;
            int localY = localIndex >> ChunkShift;
            return new FieldCell2D((chunkX * ChunkSizeCells) + localX, (chunkY * ChunkSizeCells) + localY);
        }

        public static long PackChunkKey(int chunkX, int chunkY)
        {
            return ((long)chunkX << 32) ^ (uint)chunkY;
        }

        private static int CalculateShift(int chunkSize)
        {
            int shift = 0;
            int value = chunkSize;
            while (value > 1)
            {
                value >>= 1;
                shift++;
            }

            return shift;
        }
    }
}
