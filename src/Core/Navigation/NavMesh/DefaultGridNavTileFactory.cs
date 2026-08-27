using System;

namespace Ludots.Core.Navigation.NavMesh
{
    public static class DefaultGridNavTileFactory
    {
        public const string SourceId = "flat-grid-baseline-v2";

        public static NavTile CreateFlatTile(
            int chunkX,
            int chunkY,
            int layer,
            uint tileVersion,
            int chunkSizeCells,
            int cellSizeCm,
            byte areaId = 0)
        {
            if (chunkSizeCells <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSizeCells), "Chunk size must be positive.");
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm), "Cell size must be positive.");
            int tileSizeCm = checked(chunkSizeCells * cellSizeCm);
            return CreateFlatTile(chunkX, chunkY, layer, tileVersion, tileSizeCm, tileSizeCm, chunkSizeCells, chunkSizeCells, areaId);
        }

        public static NavTile CreateFlatTile(
            int chunkX,
            int chunkY,
            int layer,
            uint tileVersion,
            int tileWidthCm,
            int tileHeightCm,
            int tileWidthCells,
            int tileHeightCells,
            byte areaId = 0,
            int boardOriginXcm = 0,
            int boardOriginZcm = 0)
        {
            if (tileWidthCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidthCm), "Tile width must be positive.");
            if (tileHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeightCm), "Tile height must be positive.");
            if (tileWidthCells <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidthCells), "Tile width in cells must be positive.");
            if (tileHeightCells <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeightCells), "Tile height in cells must be positive.");

            int originXcm = checked(boardOriginXcm + chunkX * tileWidthCm);
            int originZcm = checked(boardOriginZcm + chunkY * tileHeightCm);
            int clearanceCm = Math.Max(0, Math.Min(tileWidthCm, tileHeightCm) / 2);

            return new NavTile(
                new NavTileId(chunkX, chunkY, layer),
                tileVersion,
                buildConfigHash: 0UL,
                checksum: 0UL,
                originXcm,
                originZcm,
                vertexXcm: new[] { 0, tileWidthCm, tileWidthCm, 0 },
                vertexYcm: new[] { 0, 0, 0, 0 },
                vertexZcm: new[] { 0, 0, tileHeightCm, tileHeightCm },
                triA: new[] { 0, 1 },
                triB: new[] { 1, 2 },
                triC: new[] { 3, 3 },
                n0: new[] { -1, -1 },
                n1: new[] { 1, -1 },
                n2: new[] { -1, 0 },
                triAreaIds: new[] { areaId, areaId },
                portals: new[]
                {
                    new NavBorderPortal(NavPortalSide.West, 0, 0, 0, checked((short)tileHeightCells), 0, 0, 0, tileHeightCm, clearanceCm),
                    new NavBorderPortal(NavPortalSide.East, checked((short)tileWidthCells), 0, checked((short)tileWidthCells), checked((short)tileHeightCells), tileWidthCm, 0, tileWidthCm, tileHeightCm, clearanceCm),
                    new NavBorderPortal(NavPortalSide.North, 0, 0, checked((short)tileWidthCells), 0, 0, 0, tileWidthCm, 0, clearanceCm),
                    new NavBorderPortal(NavPortalSide.South, 0, checked((short)tileHeightCells), checked((short)tileWidthCells), checked((short)tileHeightCells), 0, tileHeightCm, tileWidthCm, tileHeightCm, clearanceCm),
                });
        }
    }
}
