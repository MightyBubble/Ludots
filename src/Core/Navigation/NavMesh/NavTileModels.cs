using System;

namespace Ludots.Core.Navigation.NavMesh
{
    public enum NavPortalSide : byte
    {
        West = 0,
        East = 1,
        North = 2,
        South = 3
    }

    public readonly struct NavTileId : IEquatable<NavTileId>
    {
        public readonly int ChunkX;
        public readonly int ChunkY;
        public readonly int Layer;

        public NavTileId(int chunkX, int chunkY, int layer = 0)
        {
            ChunkX = chunkX;
            ChunkY = chunkY;
            Layer = layer;
        }

        public bool Equals(NavTileId other) => ChunkX == other.ChunkX && ChunkY == other.ChunkY && Layer == other.Layer;
        public override bool Equals(object obj) => obj is NavTileId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ChunkX, ChunkY, Layer);
        public override string ToString() => $"{ChunkX},{ChunkY},{Layer}";
    }

    public sealed class NavTile
    {
        public readonly NavTileId TileId;
        public readonly uint TileVersion;
        public readonly ulong BuildConfigHash;
        public readonly ulong Checksum;

        public readonly int OriginXcm;
        public readonly int OriginZcm;

        public readonly int[] VertexXcm;
        public readonly int[] VertexYcm;
        public readonly int[] VertexZcm;

        public readonly int[] TriA;
        public readonly int[] TriB;
        public readonly int[] TriC;

        public readonly int[] N0;
        public readonly int[] N1;
        public readonly int[] N2;

        public readonly byte[] TriAreaIds;

        public readonly NavBorderPortal[] Portals;

        public int VertexCount => VertexXcm.Length;
        public int TriangleCount => TriA.Length;

        public NavTile(
            NavTileId tileId,
            uint tileVersion,
            ulong buildConfigHash,
            ulong checksum,
            int originXcm,
            int originZcm,
            int[] vertexXcm,
            int[] vertexYcm,
            int[] vertexZcm,
            int[] triA,
            int[] triB,
            int[] triC,
            int[] n0,
            int[] n1,
            int[] n2,
            NavBorderPortal[] portals)
            : this(
                tileId,
                tileVersion,
                buildConfigHash,
                checksum,
                originXcm,
                originZcm,
                vertexXcm,
                vertexYcm,
                vertexZcm,
                triA,
                triB,
                triC,
                n0,
                n1,
                n2,
                triAreaIds: null,
                portals)
        {
        }

        public NavTile(
            NavTileId tileId,
            uint tileVersion,
            ulong buildConfigHash,
            ulong checksum,
            int originXcm,
            int originZcm,
            int[] vertexXcm,
            int[] vertexYcm,
            int[] vertexZcm,
            int[] triA,
            int[] triB,
            int[] triC,
            int[] n0,
            int[] n1,
            int[] n2,
            byte[] triAreaIds,
            NavBorderPortal[] portals)
        {
            TileId = tileId;
            TileVersion = tileVersion;
            BuildConfigHash = buildConfigHash;
            Checksum = checksum;
            OriginXcm = originXcm;
            OriginZcm = originZcm;
            VertexXcm = vertexXcm ?? throw new ArgumentNullException(nameof(vertexXcm));
            VertexYcm = vertexYcm ?? throw new ArgumentNullException(nameof(vertexYcm));
            VertexZcm = vertexZcm ?? throw new ArgumentNullException(nameof(vertexZcm));
            TriA = triA ?? throw new ArgumentNullException(nameof(triA));
            TriB = triB ?? throw new ArgumentNullException(nameof(triB));
            TriC = triC ?? throw new ArgumentNullException(nameof(triC));
            N0 = n0 ?? throw new ArgumentNullException(nameof(n0));
            N1 = n1 ?? throw new ArgumentNullException(nameof(n1));
            N2 = n2 ?? throw new ArgumentNullException(nameof(n2));
            TriAreaIds = triAreaIds ?? new byte[TriA.Length];
            Portals = portals ?? throw new ArgumentNullException(nameof(portals));
        }
    }

    public readonly struct NavBorderPortal
    {
        public readonly NavPortalSide Side;
        public readonly short U0;
        public readonly short V0;
        public readonly short U1;
        public readonly short V1;
        public readonly int LeftXcm;
        public readonly int LeftZcm;
        public readonly int RightXcm;
        public readonly int RightZcm;
        public readonly int ClearanceCm;

        public NavBorderPortal(
            NavPortalSide side,
            short u0,
            short v0,
            short u1,
            short v1,
            int leftXcm,
            int leftZcm,
            int rightXcm,
            int rightZcm,
            int clearanceCm)
        {
            Side = side;
            U0 = u0;
            V0 = v0;
            U1 = u1;
            V1 = v1;
            LeftXcm = leftXcm;
            LeftZcm = leftZcm;
            RightXcm = rightXcm;
            RightZcm = rightZcm;
            ClearanceCm = clearanceCm;
        }
    }

    public enum NavBakeStage : byte
    {
        None = 0,
        Walkability = 1,
        WalkMask = 2,
        Contour = 3,
        Polygon = 4,
        Triangulate = 5,
        Adjacency = 6,
        Clearance = 7,
        Portal = 8,
        Serialize = 9
    }

    public enum NavBakeErrorCode : ushort
    {
        None = 0,
        InvalidInput = 1,
        NoWalkableDomain = 2,
        TriangulationFailed = 3,
        SerializationFailed = 4,
        ContourFailed = 5,
        PolygonFailed = 6,
        TriangulateFailed = 7
    }

    public readonly struct NavBakeArtifact
    {
        public readonly NavTileId TileId;
        public readonly uint TileVersion;
        public readonly NavBakeStage Stage;
        public readonly NavBakeErrorCode ErrorCode;
        public readonly string Message;
        public readonly int WalkableTriangleCount;
        public readonly int VertexCount;
        public readonly int TriangleCount;
        public readonly int PortalCount;
        public readonly string[] DebugLog;

        public NavBakeArtifact(
            NavTileId tileId,
            uint tileVersion,
            NavBakeStage stage,
            NavBakeErrorCode errorCode,
            string message,
            int walkableTriangleCount,
            int vertexCount,
            int triangleCount,
            int portalCount,
            string[] debugLog = null)
        {
            TileId = tileId;
            TileVersion = tileVersion;
            Stage = stage;
            ErrorCode = errorCode;
            Message = message ?? "";
            WalkableTriangleCount = walkableTriangleCount;
            VertexCount = vertexCount;
            TriangleCount = triangleCount;
            PortalCount = portalCount;
            DebugLog = debugLog;
        }
    }

    public readonly struct NavBuildConfig
    {
        public readonly float HeightScaleMeters;
        public readonly float MinWalkableUpDot;
        public readonly int CliffHeightThreshold;

        public NavBuildConfig(float heightScaleMeters, float minWalkableUpDot, int cliffHeightThreshold)
        {
            HeightScaleMeters = heightScaleMeters;
            MinWalkableUpDot = minWalkableUpDot;
            CliffHeightThreshold = cliffHeightThreshold;
        }

        public ulong ComputeHash()
        {
            ulong h = 1469598103934665603UL;
            h = (h ^ (ulong)BitConverter.SingleToInt32Bits(HeightScaleMeters)) * 1099511628211UL;
            h = (h ^ (ulong)BitConverter.SingleToInt32Bits(MinWalkableUpDot)) * 1099511628211UL;
            h = (h ^ (ulong)CliffHeightThreshold) * 1099511628211UL;
            return h;
        }
    }
}
