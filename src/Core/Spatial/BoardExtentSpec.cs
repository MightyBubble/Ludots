using System;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// Board extent authored in topology-native units (cells x cell metric).
    /// Produces the board's world AABB; centered until board placement (#1567 slice 2).
    /// </summary>
    public readonly struct BoardExtentSpec : IEquatable<BoardExtentSpec>
    {
        public readonly int WidthCells;
        public readonly int HeightCells;
        public readonly int CellSizeCm;

        public BoardExtentSpec(int widthCells, int heightCells, int cellSizeCm)
        {
            if (widthCells <= 0) throw new ArgumentOutOfRangeException(nameof(widthCells));
            if (heightCells <= 0) throw new ArgumentOutOfRangeException(nameof(heightCells));
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));

            WidthCells = widthCells;
            HeightCells = heightCells;
            CellSizeCm = cellSizeCm;
        }

        public int WidthCm => checked(WidthCells * CellSizeCm);
        public int HeightCm => checked(HeightCells * CellSizeCm);

        public WorldSizeSpec ToWorldSizeSpec()
        {
            int widthCm = WidthCm;
            int heightCm = HeightCm;
            return new WorldSizeSpec(
                new WorldAabbCm(-widthCm / 2, -heightCm / 2, widthCm, heightCm),
                CellSizeCm);
        }

        public bool Equals(BoardExtentSpec other)
            => WidthCells == other.WidthCells &&
               HeightCells == other.HeightCells &&
               CellSizeCm == other.CellSizeCm;

        public override bool Equals(object obj) => obj is BoardExtentSpec other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(WidthCells, HeightCells, CellSizeCm);
        public static bool operator ==(BoardExtentSpec left, BoardExtentSpec right) => left.Equals(right);
        public static bool operator !=(BoardExtentSpec left, BoardExtentSpec right) => !left.Equals(right);
        public override string ToString()
            => $"{WidthCells}x{HeightCells} cells, Cell={CellSizeCm}cm";
    }
}
