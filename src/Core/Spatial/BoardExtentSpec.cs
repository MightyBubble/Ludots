using System;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Spatial
{
    /// <summary>
    /// Authored board rectangle in centimeters, placed by the world position of local (0, 0).
    /// Cell counts are the whole cells that fit from that corner; leftover centimeters stay on the far side.
    /// </summary>
    public readonly struct BoardExtentSpec : IEquatable<BoardExtentSpec>
    {
        public readonly int WidthCm;
        public readonly int HeightCm;
        public readonly int CellSizeCm;
        public readonly int TopologyOriginXCm;
        public readonly int TopologyOriginYCm;

        public BoardExtentSpec(int widthCm, int heightCm, int cellSizeCm, int topologyOriginXCm, int topologyOriginYCm)
        {
            if (widthCm <= 0) throw new ArgumentOutOfRangeException(nameof(widthCm));
            if (heightCm <= 0) throw new ArgumentOutOfRangeException(nameof(heightCm));
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            if (widthCm / cellSizeCm <= 0 || heightCm / cellSizeCm <= 0)
            {
                throw new ArgumentException("Board rectangle must cover at least one cell on each axis.");
            }

            WidthCm = widthCm;
            HeightCm = heightCm;
            CellSizeCm = cellSizeCm;
            TopologyOriginXCm = topologyOriginXCm;
            TopologyOriginYCm = topologyOriginYCm;
        }

        public int WidthCells => WidthCm / CellSizeCm;
        public int HeightCells => HeightCm / CellSizeCm;

        public WorldAabbCm ToWorldAabb()
        {
            return new WorldAabbCm(TopologyOriginXCm, TopologyOriginYCm, WidthCm, HeightCm);
        }

        public WorldSizeSpec ToWorldSizeSpec() => new(ToWorldAabb(), CellSizeCm);

        public bool Equals(BoardExtentSpec other)
            => WidthCm == other.WidthCm &&
               HeightCm == other.HeightCm &&
               CellSizeCm == other.CellSizeCm &&
               TopologyOriginXCm == other.TopologyOriginXCm &&
               TopologyOriginYCm == other.TopologyOriginYCm;

        public override bool Equals(object obj) => obj is BoardExtentSpec other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(WidthCm, HeightCm, CellSizeCm, TopologyOriginXCm, TopologyOriginYCm);
        public static bool operator ==(BoardExtentSpec left, BoardExtentSpec right) => left.Equals(right);
        public static bool operator !=(BoardExtentSpec left, BoardExtentSpec right) => !left.Equals(right);
        public override string ToString()
            => $"{WidthCm}x{HeightCm}cm, Cell={CellSizeCm}cm, TopologyOrigin=({TopologyOriginXCm},{TopologyOriginYCm})";
    }
}
