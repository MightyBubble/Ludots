using System;

namespace Ludots.Core.Navigation.NavMesh
{
    /// <summary>
    /// Shared construction path for a legitimate fully-blocked / no-walkable-domain NavTile.
    /// Empty output is success: non-null tile with zero topology and a real binary checksum.
    /// </summary>
    public static class NavValidEmptyTile
    {
        public const string DefaultMessage = "No walkable domain.";

        /// <summary>Serialized size of a zero-topology tile (header + zero counts).</summary>
        public const int EmptySerializedByteCount = 4 + 2 + 2 + 4 + 4 + 4 + 4 + 8 + 8 + 4 + 4 + 4 + 4 + 4 + 4 + 4;

        public static NavTile Create(
            NavTileId tileId,
            uint tileVersion,
            ulong buildConfigHash,
            int originXcm,
            int originZcm)
        {
            var tile = new NavTile(
                tileId,
                tileVersion,
                buildConfigHash,
                checksum: 0UL,
                originXcm,
                originZcm,
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<byte>(),
                Array.Empty<NavBorderPortal>());
            Span<byte> scratch = stackalloc byte[EmptySerializedByteCount];
            NavTileBinary.AssignChecksum(tile, scratch);
            return tile;
        }

        public static void Fill(
            NavTile destination,
            NavTileId tileId,
            uint tileVersion,
            ulong buildConfigHash,
            int originXcm,
            int originZcm,
            Span<byte> checksumScratch)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.AssignHeader(tileId, tileVersion, buildConfigHash, originXcm, originZcm);
            destination.ClearTopology();
            NavTileBinary.AssignChecksum(destination, checksumScratch);
        }

        public static NavBakeArtifact CreateSuccessArtifact(
            NavTile tile,
            string message = DefaultMessage,
            string[] debugLog = null)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            if (tile.VertexCount != 0 ||
                tile.TriangleCount != 0 ||
                tile.PortalCount != 0)
            {
                throw new InvalidOperationException(
                    "NavValidEmptyTile success artifact requires a zero-topology tile.");
            }

            return new NavBakeArtifact(
                tile.TileId,
                tile.TileVersion,
                NavBakeStage.Serialize,
                NavBakeErrorCode.None,
                message ?? DefaultMessage,
                walkableTriangleCount: 0,
                vertexCount: 0,
                triangleCount: 0,
                portalCount: 0,
                debugLog);
        }
    }
}
