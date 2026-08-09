using System;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// Authoritative world-to-tile projection contract for nav queries.
    /// Owned by <see cref="NavQueryServiceRegistry"/> and sourced from the active
    /// <see cref="NavTriangleSurfaceTileGrid"/> (origin + tile extents), never from silent Hex defaults.
    /// </summary>
    public readonly struct NavQueryTileSpace : IEquatable<NavQueryTileSpace>
    {
        public NavQueryTileSpace(int originXcm, int originZcm, int tileWidthCm, int tileHeightCm)
        {
            if (tileWidthCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileWidthCm), tileWidthCm, "Tile width must be positive.");
            }

            if (tileHeightCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileHeightCm), tileHeightCm, "Tile height must be positive.");
            }

            OriginXcm = originXcm;
            OriginZcm = originZcm;
            TileWidthCm = tileWidthCm;
            TileHeightCm = tileHeightCm;
        }

        public int OriginXcm { get; }

        public int OriginZcm { get; }

        public int TileWidthCm { get; }

        public int TileHeightCm { get; }

        public static NavQueryTileSpace FromGrid(in NavTriangleSurfaceTileGrid grid)
        {
            return new NavQueryTileSpace(
                grid.OriginXcm,
                grid.OriginZcm,
                grid.TileWidthCm,
                grid.TileHeightCm);
        }

        public bool Equals(NavQueryTileSpace other)
        {
            return OriginXcm == other.OriginXcm
                && OriginZcm == other.OriginZcm
                && TileWidthCm == other.TileWidthCm
                && TileHeightCm == other.TileHeightCm;
        }

        public override bool Equals(object obj) => obj is NavQueryTileSpace other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(OriginXcm, OriginZcm, TileWidthCm, TileHeightCm);
    }
}
