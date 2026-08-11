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
            byte areaId = 0)
        {
            if (tileWidthCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidthCm), "Tile width must be positive.");
            if (tileHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeightCm), "Tile height must be positive.");
            if (tileWidthCells <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidthCells), "Tile width in cells must be positive.");
            if (tileHeightCells <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeightCells), "Tile height in cells must be positive.");

            int originXcm = checked(chunkX * tileWidthCm);
            int originZcm = checked(chunkY * tileHeightCm);
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
                    new NavBorderPortal(NavPortalSide.West, 0, 0, 0, checked((short)tileHeightCells), 0, 0, 0, 0, 0, tileHeightCm, clearanceCm),
                    new NavBorderPortal(NavPortalSide.East, checked((short)tileWidthCells), 0, checked((short)tileWidthCells), checked((short)tileHeightCells), tileWidthCm, 0, 0, tileWidthCm, 0, tileHeightCm, clearanceCm),
                    new NavBorderPortal(NavPortalSide.North, 0, 0, checked((short)tileWidthCells), 0, 0, 0, 0, tileWidthCm, 0, 0, clearanceCm),
                    new NavBorderPortal(NavPortalSide.South, 0, checked((short)tileHeightCells), checked((short)tileWidthCells), checked((short)tileHeightCells), 0, 0, tileHeightCm, tileWidthCm, 0, tileHeightCm, clearanceCm),
                });
        }

        /// <summary>
        /// Writes the flat-grid-baseline-v2 footprint into a caller-owned banked tile without allocating.
        /// The caller owns checksum assignment after any height or build-header changes.
        /// </summary>
        public static void FillFlatTile(
            NavTile destination,
            int chunkX,
            int chunkY,
            int layer,
            uint tileVersion,
            ulong buildConfigHash,
            int originXcm,
            int originZcm,
            int tileWidthCm,
            int tileHeightCm,
            int tileWidthCells,
            int tileHeightCells,
            int floorYcm,
            byte areaId = 0)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (tileWidthCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidthCm), "Tile width must be positive.");
            if (tileHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeightCm), "Tile height must be positive.");
            if (tileWidthCells <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidthCells), "Tile width in cells must be positive.");
            if (tileHeightCells <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeightCells), "Tile height in cells must be positive.");

            destination.SetCounts(vertexCount: 4, triangleCount: 2, portalCount: 4);
            destination.AssignHeader(
                new NavTileId(chunkX, chunkY, layer),
                tileVersion,
                buildConfigHash,
                originXcm,
                originZcm);

            destination.VertexXcm[0] = 0;
            destination.VertexZcm[0] = 0;
            destination.VertexXcm[1] = tileWidthCm;
            destination.VertexZcm[1] = 0;
            destination.VertexXcm[2] = tileWidthCm;
            destination.VertexZcm[2] = tileHeightCm;
            destination.VertexXcm[3] = 0;
            destination.VertexZcm[3] = tileHeightCm;
            for (int i = 0; i < 4; i++)
            {
                destination.VertexYcm[i] = floorYcm;
            }

            destination.TriA[0] = 0;
            destination.TriB[0] = 1;
            destination.TriC[0] = 3;
            destination.N0[0] = -1;
            destination.N1[0] = 1;
            destination.N2[0] = -1;
            destination.TriAreaIds[0] = areaId;

            destination.TriA[1] = 1;
            destination.TriB[1] = 2;
            destination.TriC[1] = 3;
            destination.N0[1] = -1;
            destination.N1[1] = -1;
            destination.N2[1] = 0;
            destination.TriAreaIds[1] = areaId;

            int clearanceCm = Math.Max(0, Math.Min(tileWidthCm, tileHeightCm) / 2);
            destination.Portals[0] = new NavBorderPortal(
                NavPortalSide.West, 0, 0, 0, checked((short)tileHeightCells),
                0, floorYcm, 0, 0, floorYcm, tileHeightCm, clearanceCm);
            destination.Portals[1] = new NavBorderPortal(
                NavPortalSide.East, checked((short)tileWidthCells), 0, checked((short)tileWidthCells), checked((short)tileHeightCells),
                tileWidthCm, floorYcm, 0, tileWidthCm, floorYcm, tileHeightCm, clearanceCm);
            destination.Portals[2] = new NavBorderPortal(
                NavPortalSide.North, 0, 0, checked((short)tileWidthCells), 0,
                0, floorYcm, 0, tileWidthCm, floorYcm, 0, clearanceCm);
            destination.Portals[3] = new NavBorderPortal(
                NavPortalSide.South, 0, checked((short)tileHeightCells), checked((short)tileWidthCells), checked((short)tileHeightCells),
                0, floorYcm, tileHeightCm, tileWidthCm, floorYcm, tileHeightCm, clearanceCm);
            destination.SetChecksum(0UL);
        }

        /// <summary>
        /// True when <paramref name="tile"/> matches the flat-grid-baseline-v2 footprint
        /// (four corners + two tris) used by Editor Bridge bootstrap and Detour baseline query.
        /// </summary>
        public static bool MatchesFlatBaselineFootprint(NavTile tile, int tileWidthCm, int tileHeightCm)
        {
            if (tile == null || tileWidthCm <= 0 || tileHeightCm <= 0)
            {
                return false;
            }

            if (tile.VertexCount != 4 || tile.TriangleCount != 2 || tile.PortalCount != 4)
            {
                return false;
            }

            return tile.VertexXcm[0] == 0 && tile.VertexZcm[0] == 0 &&
                   tile.VertexXcm[1] == tileWidthCm && tile.VertexZcm[1] == 0 &&
                   tile.VertexXcm[2] == tileWidthCm && tile.VertexZcm[2] == tileHeightCm &&
                   tile.VertexXcm[3] == 0 && tile.VertexZcm[3] == tileHeightCm;
        }
    }
}
