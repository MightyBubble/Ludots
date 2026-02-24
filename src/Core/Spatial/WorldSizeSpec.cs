using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    public readonly struct WorldSizeSpec : IEquatable<WorldSizeSpec>
    {
        public readonly WorldAabbCm Bounds;
        public readonly int GridCellSizeCm;

        public WorldSizeSpec(WorldAabbCm bounds, int gridCellSizeCm)
        {
            if (gridCellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(gridCellSizeCm));
            Bounds = bounds;
            GridCellSizeCm = gridCellSizeCm;
        }

        public bool Contains(in WorldCmInt2 p)
        {
            return p.X >= Bounds.Left && p.X <= Bounds.Right && p.Y >= Bounds.Top && p.Y <= Bounds.Bottom;
        }

        public bool Equals(WorldSizeSpec other) => Bounds.Equals(other.Bounds) && GridCellSizeCm == other.GridCellSizeCm;
        public override bool Equals(object obj) => obj is WorldSizeSpec other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Bounds, GridCellSizeCm);
        public static bool operator ==(WorldSizeSpec left, WorldSizeSpec right) => left.Equals(right);
        public static bool operator !=(WorldSizeSpec left, WorldSizeSpec right) => !left.Equals(right);
        public override string ToString() => $"{Bounds}, Cell={GridCellSizeCm}cm";
    }
}
