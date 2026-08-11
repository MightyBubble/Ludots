using System;
using Ludots.Core.Navigation.NavMesh.Bake;

namespace Ludots.Core.Navigation.NavMesh.Surface
{
    /// <summary>
    /// Deterministic tile-local CSR over <see cref="NavTriangleSurfaceSnapshot"/>.
    /// Cold construction allocates owned CSR arrays; warmed tile lookup allocates zero managed bytes.
    /// Tile coverage is half-open on each axis ([min, max)); triangle XZ AABBs (after halo expansion) are closed.
    /// An exact tile-boundary triangle therefore assigns to both adjacent tiles; this is intentional and conservative.
    /// A triangle whose halo-expanded XZ AABB does not intersect the declared grid fails fast (no silent skip).
    /// Partially intersecting triangles clamp to the grid and continue.
    /// </summary>
    public sealed class NavTriangleSurfaceTileIndex
    {
        private readonly int[] _tileOffsets;
        private readonly int[] _triangleIndices;

        private NavTriangleSurfaceTileIndex(
            NavTriangleSurfaceSnapshot surface,
            NavTriangleSurfaceTileGrid grid,
            int[] tileOffsets,
            int[] triangleIndices)
        {
            Surface = surface;
            Grid = grid;
            _tileOffsets = tileOffsets;
            _triangleIndices = triangleIndices;
        }

        public NavTriangleSurfaceSnapshot Surface { get; }

        public NavTriangleSurfaceTileGrid Grid { get; }

        public static NavTriangleSurfaceTileIndex Build(
            NavTriangleSurfaceSnapshot surface,
            NavTriangleSurfaceTileGrid grid)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int tileCount = grid.TileCount;
            int triangleCount = surface.TriangleCount;

            int[] order = triangleCount == 0 ? Array.Empty<int>() : new int[triangleCount];
            int[] stableSortKeys = triangleCount == 0 ? Array.Empty<int>() : new int[triangleCount];
            ReadOnlySpan<int> stableIds = surface.TriStableIds;
            for (int i = 0; i < triangleCount; i++)
            {
                order[i] = i;
                stableSortKeys[i] = stableIds[i];
            }

            // Sort by stable id so CSR fill order (and thus per-tile order) is independent of source input order.
            Array.Sort(stableSortKeys, order);

            int[] counts = new int[tileCount];
            ReadOnlySpan<int> vertexXcm = surface.VertexXcm;
            ReadOnlySpan<int> vertexZcm = surface.VertexZcm;
            ReadOnlySpan<int> triA = surface.TriA;
            ReadOnlySpan<int> triB = surface.TriB;
            ReadOnlySpan<int> triC = surface.TriC;
            int halo = grid.HaloPaddingCm;
            int originX = grid.OriginXcm;
            int originZ = grid.OriginZcm;
            int tileWidth = grid.TileWidthCm;
            int tileHeight = grid.TileHeightCm;
            int tileCountX = grid.TileCountX;
            int tileCountZ = grid.TileCountZ;

            for (int oi = 0; oi < order.Length; oi++)
            {
                int tri = order[oi];
                if (!TryGetOverlappingTileRange(
                        vertexXcm,
                        vertexZcm,
                        triA[tri],
                        triB[tri],
                        triC[tri],
                        halo,
                        originX,
                        originZ,
                        tileWidth,
                        tileHeight,
                        tileCountX,
                        tileCountZ,
                        out int minTileX,
                        out int maxTileX,
                        out int minTileZ,
                        out int maxTileZ))
                {
                    throw new ArgumentException(
                        $"Triangle index {tri} (stable id {stableIds[tri]}) halo-expanded XZ AABB does not intersect the declared tile grid " +
                        $"(origin=({originX},{originZ}), tileSize=({tileWidth},{tileHeight}), tileCount=({tileCountX},{tileCountZ}), halo={halo}).",
                        nameof(surface));
                }

                for (int tz = minTileZ; tz <= maxTileZ; tz++)
                {
                    int row = checked(tz * tileCountX);
                    for (int tx = minTileX; tx <= maxTileX; tx++)
                    {
                        counts[checked(row + tx)] = checked(counts[row + tx] + 1);
                    }
                }
            }

            int[] offsets = new int[checked(tileCount + 1)];
            int sum = 0;
            for (int i = 0; i < tileCount; i++)
            {
                offsets[i] = sum;
                sum = checked(sum + counts[i]);
            }

            offsets[tileCount] = sum;

            int[] indices = sum == 0 ? Array.Empty<int>() : new int[sum];
            int[] cursor = new int[tileCount];
            Array.Copy(offsets, cursor, tileCount);

            for (int oi = 0; oi < order.Length; oi++)
            {
                int tri = order[oi];
                if (!TryGetOverlappingTileRange(
                        vertexXcm,
                        vertexZcm,
                        triA[tri],
                        triB[tri],
                        triC[tri],
                        halo,
                        originX,
                        originZ,
                        tileWidth,
                        tileHeight,
                        tileCountX,
                        tileCountZ,
                        out int minTileX,
                        out int maxTileX,
                        out int minTileZ,
                        out int maxTileZ))
                {
                    // Count pass already validated intersection; this path must not silently skip.
                    throw new InvalidOperationException(
                        $"Triangle index {tri} (stable id {stableIds[tri]}) lost grid intersection between CSR count and fill passes.");
                }

                for (int tz = minTileZ; tz <= maxTileZ; tz++)
                {
                    int row = checked(tz * tileCountX);
                    for (int tx = minTileX; tx <= maxTileX; tx++)
                    {
                        int tileIndex = checked(row + tx);
                        indices[cursor[tileIndex]++] = tri;
                    }
                }
            }

            return new NavTriangleSurfaceTileIndex(surface, grid, offsets, indices);
        }

        public ReadOnlySpan<int> GetTriangleIndices(int tileX, int tileZ)
        {
            int tileCountX = Grid.TileCountX;
            int tileCountZ = Grid.TileCountZ;
            if ((uint)tileX >= (uint)tileCountX)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tileX),
                    tileX,
                    $"Tile X {tileX} is outside grid width {tileCountX}.");
            }

            if ((uint)tileZ >= (uint)tileCountZ)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tileZ),
                    tileZ,
                    $"Tile Z {tileZ} is outside grid height {tileCountZ}.");
            }

            int tileIndex = checked(tileZ * tileCountX + tileX);
            int start = _tileOffsets[tileIndex];
            int end = _tileOffsets[tileIndex + 1];
            return _triangleIndices.AsSpan(start, end - start);
        }

        public ReadOnlySpan<int> GetTriangleIndices(NavBakeTileCoord tile)
            => GetTriangleIndices(tile.ChunkX, tile.ChunkY);

        /// <summary>
        /// Computes the closed triangle XZ AABB (halo-expanded), then maps it onto the half-open tile grid.
        /// Exact boundary coordinates therefore include both adjacent tiles. Returns false when there is no intersection.
        /// </summary>
        private static bool TryGetOverlappingTileRange(
            ReadOnlySpan<int> vertexXcm,
            ReadOnlySpan<int> vertexZcm,
            int a,
            int b,
            int c,
            int haloPaddingCm,
            int originXcm,
            int originZcm,
            int tileWidthCm,
            int tileHeightCm,
            int tileCountX,
            int tileCountZ,
            out int minTileX,
            out int maxTileX,
            out int minTileZ,
            out int maxTileZ)
        {
            int ax = vertexXcm[a];
            int bx = vertexXcm[b];
            int cx = vertexXcm[c];
            int az = vertexZcm[a];
            int bz = vertexZcm[b];
            int cz = vertexZcm[c];

            int minX = ax;
            if (bx < minX) minX = bx;
            if (cx < minX) minX = cx;
            int maxX = ax;
            if (bx > maxX) maxX = bx;
            if (cx > maxX) maxX = cx;

            int minZ = az;
            if (bz < minZ) minZ = bz;
            if (cz < minZ) minZ = cz;
            int maxZ = az;
            if (bz > maxZ) maxZ = bz;
            if (cz > maxZ) maxZ = cz;

            int expandedMinX = checked(minX - haloPaddingCm);
            int expandedMaxX = checked(maxX + haloPaddingCm);
            int expandedMinZ = checked(minZ - haloPaddingCm);
            int expandedMaxZ = checked(maxZ + haloPaddingCm);

            minTileX = FloorDiv(checked(expandedMinX - originXcm), tileWidthCm);
            maxTileX = FloorDiv(checked(expandedMaxX - originXcm), tileWidthCm);
            minTileZ = FloorDiv(checked(expandedMinZ - originZcm), tileHeightCm);
            maxTileZ = FloorDiv(checked(expandedMaxZ - originZcm), tileHeightCm);

            if (maxTileX < 0 || maxTileZ < 0 || minTileX >= tileCountX || minTileZ >= tileCountZ)
            {
                minTileX = 0;
                maxTileX = -1;
                minTileZ = 0;
                maxTileZ = -1;
                return false;
            }

            if (minTileX < 0) minTileX = 0;
            if (minTileZ < 0) minTileZ = 0;
            if (maxTileX >= tileCountX) maxTileX = tileCountX - 1;
            if (maxTileZ >= tileCountZ) maxTileZ = tileCountZ - 1;
            return true;
        }

        private static int FloorDiv(int dividend, int divisor)
        {
            int quotient = dividend / divisor;
            if (dividend < 0 && (dividend % divisor) != 0)
            {
                quotient--;
            }

            return quotient;
        }
    }
}
