using System;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// Board extent authored in topology-native units (cells x cell metric).
    /// Produces the board's world AABB: centered by default (legacy frame, grid axes
    /// pinned at world 0), or anchored at a declared min-corner (#1567 slice 2b —
    /// anchored boards pin their grid frame to that corner).
    /// </summary>
    public readonly struct BoardExtentSpec : IEquatable<BoardExtentSpec>
    {
        public readonly int WidthCells;
        public readonly int HeightCells;
        public readonly int CellSizeCm;
        public readonly int? OriginXCm;
        public readonly int? OriginYCm;

        public BoardExtentSpec(int widthCells, int heightCells, int cellSizeCm, int? originXCm = null, int? originYCm = null)
        {
            if (widthCells <= 0) throw new ArgumentOutOfRangeException(nameof(widthCells));
            if (heightCells <= 0) throw new ArgumentOutOfRangeException(nameof(heightCells));
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            if ((originXCm is null) != (originYCm is null))
                throw new ArgumentException("Board origin must be authored on both axes together.");

            WidthCells = widthCells;
            HeightCells = heightCells;
            CellSizeCm = cellSizeCm;
            OriginXCm = originXCm;
            OriginYCm = originYCm;
        }

        public int WidthCm => checked(WidthCells * CellSizeCm);
        public int HeightCm => checked(HeightCells * CellSizeCm);

        /// <summary>
        /// Conservative world-cm footprint (e.g. a hex grid's bounds) padded up to whole
        /// cells; authored cm need not be cell multiples.
        /// </summary>
        public static BoardExtentSpec FromConservativeCm(int widthCm, int heightCm, int cellSizeCm, int? originXCm = null, int? originYCm = null)
        {
            if (widthCm <= 0) throw new ArgumentOutOfRangeException(nameof(widthCm));
            if (heightCm <= 0) throw new ArgumentOutOfRangeException(nameof(heightCm));
            return new BoardExtentSpec(
                (widthCm + cellSizeCm - 1) / cellSizeCm,
                (heightCm + cellSizeCm - 1) / cellSizeCm,
                cellSizeCm,
                originXCm,
                originYCm);
        }

        public bool IsAnchored => OriginXCm is not null;

        public WorldAabbCm ToWorldAabb()
        {
            int widthCm = WidthCm;
            int heightCm = HeightCm;
            return new WorldAabbCm(
                OriginXCm ?? -widthCm / 2,
                OriginYCm ?? -heightCm / 2,
                widthCm,
                heightCm);
        }

        public WorldSizeSpec ToWorldSizeSpec() => new(ToWorldAabb(), CellSizeCm);

        public bool Equals(BoardExtentSpec other)
            => WidthCells == other.WidthCells &&
               HeightCells == other.HeightCells &&
               CellSizeCm == other.CellSizeCm &&
               OriginXCm == other.OriginXCm &&
               OriginYCm == other.OriginYCm;

        public override bool Equals(object obj) => obj is BoardExtentSpec other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(WidthCells, HeightCells, CellSizeCm, OriginXCm, OriginYCm);
        public static bool operator ==(BoardExtentSpec left, BoardExtentSpec right) => left.Equals(right);
        public static bool operator !=(BoardExtentSpec left, BoardExtentSpec right) => !left.Equals(right);
        public override string ToString()
            => $"{WidthCells}x{HeightCells} cells, Cell={CellSizeCm}cm" +
               (IsAnchored ? $", Origin=({OriginXCm},{OriginYCm})cm" : ", centered");
    }
}
