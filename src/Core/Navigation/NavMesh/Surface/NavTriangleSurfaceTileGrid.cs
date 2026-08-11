using System;

namespace Ludots.Core.Navigation.NavMesh.Surface
{
    /// <summary>
    /// Explicit tile grid used to build a deterministic tile-local CSR over a triangle surface.
    /// Halo/padding expands each triangle's closed XZ AABB before assignment against half-open tile coverage.
    /// </summary>
    public readonly struct NavTriangleSurfaceTileGrid
    {
        public NavTriangleSurfaceTileGrid(
            int originXcm,
            int originZcm,
            int tileWidthCm,
            int tileHeightCm,
            int tileCountX,
            int tileCountZ,
            int haloPaddingCm)
        {
            if (tileWidthCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileWidthCm), tileWidthCm, "Tile width must be positive.");
            }

            if (tileHeightCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileHeightCm), tileHeightCm, "Tile height must be positive.");
            }

            if (tileCountX <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileCountX), tileCountX, "Tile count X must be positive.");
            }

            if (tileCountZ <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileCountZ), tileCountZ, "Tile count Z must be positive.");
            }

            if (haloPaddingCm < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(haloPaddingCm), haloPaddingCm, "Halo/padding must be nonnegative.");
            }

            // Fail fast on overflow before the index allocates CSR storage.
            _ = checked(tileCountX * tileCountZ);
            _ = checked(originXcm + checked(tileCountX * tileWidthCm));
            _ = checked(originZcm + checked(tileCountZ * tileHeightCm));

            OriginXcm = originXcm;
            OriginZcm = originZcm;
            TileWidthCm = tileWidthCm;
            TileHeightCm = tileHeightCm;
            TileCountX = tileCountX;
            TileCountZ = tileCountZ;
            HaloPaddingCm = haloPaddingCm;
        }

        public int OriginXcm { get; }

        public int OriginZcm { get; }

        public int TileWidthCm { get; }

        public int TileHeightCm { get; }

        public int TileCountX { get; }

        public int TileCountZ { get; }

        public int HaloPaddingCm { get; }

        public int TileCount => checked(TileCountX * TileCountZ);
    }
}
