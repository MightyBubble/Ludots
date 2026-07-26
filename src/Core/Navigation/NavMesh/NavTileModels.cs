using System;
using System.Runtime.CompilerServices;

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

    /// <summary>
    /// Shared NavTile artifact. Offline constructors may own exact-fit arrays.
    /// Runtime banks preallocate capacity channels and fill counts in place without reallocating.
    /// </summary>
    public sealed class NavTile
    {
        private NavTileId _tileId;
        private uint _tileVersion;
        private ulong _buildConfigHash;
        private ulong _checksum;
        private int _originXcm;
        private int _originZcm;
        private int _vertexCount;
        private int _triangleCount;
        private int _portalCount;

        public NavTileId TileId => _tileId;
        public uint TileVersion => _tileVersion;
        public ulong BuildConfigHash => _buildConfigHash;
        public ulong Checksum => _checksum;

        public int OriginXcm => _originXcm;
        public int OriginZcm => _originZcm;

        public int[] VertexXcm { get; }
        public int[] VertexYcm { get; }
        public int[] VertexZcm { get; }

        public int[] TriA { get; }
        public int[] TriB { get; }
        public int[] TriC { get; }

        public int[] N0 { get; }
        public int[] N1 { get; }
        public int[] N2 { get; }

        public byte[] TriAreaIds { get; }

        public NavBorderPortal[] Portals { get; }

        public int VertexCapacity => VertexXcm.Length;
        public int TriangleCapacity => TriA.Length;
        public int PortalCapacity => Portals.Length;

        public int VertexCount => _vertexCount;
        public int TriangleCount => _triangleCount;
        public int PortalCount => _portalCount;

        /// <summary>
        /// Exact byte size of all preallocated channel payload arrays owned by this tile
        /// (VertexX/Y/Z, TriA/B/C, N0/N1/N2, TriAreaIds, Portals).
        /// </summary>
        public long PreallocatedChannelPayloadBytes
            => ComputeBankedChannelPayloadBytes(VertexCapacity, TriangleCapacity, PortalCapacity);

        /// <summary>Active vertex X channel; length equals <see cref="VertexCount"/>, not bank capacity.</summary>
        public ReadOnlySpan<int> ActiveVertexXcm => VertexXcm.AsSpan(0, _vertexCount);

        /// <summary>Active vertex Y channel; length equals <see cref="VertexCount"/>, not bank capacity.</summary>
        public ReadOnlySpan<int> ActiveVertexYcm => VertexYcm.AsSpan(0, _vertexCount);

        /// <summary>Active vertex Z channel; length equals <see cref="VertexCount"/>, not bank capacity.</summary>
        public ReadOnlySpan<int> ActiveVertexZcm => VertexZcm.AsSpan(0, _vertexCount);

        /// <summary>Active triangle A indices; length equals <see cref="TriangleCount"/>, not bank capacity.</summary>
        public ReadOnlySpan<int> ActiveTriA => TriA.AsSpan(0, _triangleCount);

        /// <summary>Active triangle B indices; length equals <see cref="TriangleCount"/>, not bank capacity.</summary>
        public ReadOnlySpan<int> ActiveTriB => TriB.AsSpan(0, _triangleCount);

        /// <summary>Active triangle C indices; length equals <see cref="TriangleCount"/>, not bank capacity.</summary>
        public ReadOnlySpan<int> ActiveTriC => TriC.AsSpan(0, _triangleCount);

        /// <summary>Active neighbor 0; length equals <see cref="TriangleCount"/>, not bank capacity.</summary>
        public ReadOnlySpan<int> ActiveN0 => N0.AsSpan(0, _triangleCount);

        /// <summary>Active neighbor 1; length equals <see cref="TriangleCount"/>, not bank capacity.</summary>
        public ReadOnlySpan<int> ActiveN1 => N1.AsSpan(0, _triangleCount);

        /// <summary>Active neighbor 2; length equals <see cref="TriangleCount"/>, not bank capacity.</summary>
        public ReadOnlySpan<int> ActiveN2 => N2.AsSpan(0, _triangleCount);

        /// <summary>Active triangle area ids; length equals <see cref="TriangleCount"/>, not bank capacity.</summary>
        public ReadOnlySpan<byte> ActiveTriAreaIds => TriAreaIds.AsSpan(0, _triangleCount);

        /// <summary>Active border portals; length equals <see cref="PortalCount"/>, not bank capacity.</summary>
        public ReadOnlySpan<NavBorderPortal> ActivePortals => Portals.AsSpan(0, _portalCount);

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
            VertexXcm = vertexXcm ?? throw new ArgumentNullException(nameof(vertexXcm));
            VertexYcm = vertexYcm ?? throw new ArgumentNullException(nameof(vertexYcm));
            VertexZcm = vertexZcm ?? throw new ArgumentNullException(nameof(vertexZcm));
            TriA = triA ?? throw new ArgumentNullException(nameof(triA));
            TriB = triB ?? throw new ArgumentNullException(nameof(triB));
            TriC = triC ?? throw new ArgumentNullException(nameof(triC));
            N0 = n0 ?? throw new ArgumentNullException(nameof(n0));
            N1 = n1 ?? throw new ArgumentNullException(nameof(n1));
            N2 = n2 ?? throw new ArgumentNullException(nameof(n2));
            Portals = portals ?? throw new ArgumentNullException(nameof(portals));

            if (VertexYcm.Length != VertexXcm.Length || VertexZcm.Length != VertexXcm.Length)
            {
                throw new InvalidOperationException("NavTile vertex channels must share the same capacity.");
            }

            if (TriB.Length != TriA.Length ||
                TriC.Length != TriA.Length ||
                N0.Length != TriA.Length ||
                N1.Length != TriA.Length ||
                N2.Length != TriA.Length)
            {
                throw new InvalidOperationException("NavTile triangle channels must share the same capacity.");
            }

            TriAreaIds = triAreaIds ?? new byte[TriA.Length];
            if (TriAreaIds.Length != TriA.Length)
            {
                throw new InvalidOperationException("NavTile triAreaIds capacity must match triangle capacity.");
            }

            _tileId = tileId;
            _tileVersion = tileVersion;
            _buildConfigHash = buildConfigHash;
            _checksum = checksum;
            _originXcm = originXcm;
            _originZcm = originZcm;
            _vertexCount = VertexXcm.Length;
            _triangleCount = TriA.Length;
            _portalCount = Portals.Length;
        }

        public static long ComputeBankedChannelPayloadBytes(int vertexCapacity, int triangleCapacity, int portalCapacity)
        {
            if (vertexCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCapacity), "outputVertexCapacity must be > 0.");
            }

            if (triangleCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(triangleCapacity), "outputTriangleCapacity must be > 0.");
            }

            if (portalCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(portalCapacity), "outputPortalCapacity must be > 0.");
            }

            return checked(
                (long)vertexCapacity * sizeof(int) * 3L +
                (long)triangleCapacity * sizeof(int) * 6L +
                (long)triangleCapacity * sizeof(byte) +
                (long)portalCapacity * Unsafe.SizeOf<NavBorderPortal>());
        }

        public static NavTile CreateBanked(int vertexCapacity, int triangleCapacity, int portalCapacity)
        {
            _ = ComputeBankedChannelPayloadBytes(vertexCapacity, triangleCapacity, portalCapacity);

            var tile = new NavTile(
                default,
                tileVersion: 0,
                buildConfigHash: 0UL,
                checksum: 0UL,
                originXcm: 0,
                originZcm: 0,
                new int[vertexCapacity],
                new int[vertexCapacity],
                new int[vertexCapacity],
                new int[triangleCapacity],
                new int[triangleCapacity],
                new int[triangleCapacity],
                new int[triangleCapacity],
                new int[triangleCapacity],
                new int[triangleCapacity],
                new byte[triangleCapacity],
                new NavBorderPortal[portalCapacity]);
            tile._vertexCount = 0;
            tile._triangleCount = 0;
            tile._portalCount = 0;
            return tile;
        }

        public void AssignHeader(
            NavTileId tileId,
            uint tileVersion,
            ulong buildConfigHash,
            int originXcm,
            int originZcm)
        {
            _tileId = tileId;
            _tileVersion = tileVersion;
            _buildConfigHash = buildConfigHash;
            _originXcm = originXcm;
            _originZcm = originZcm;
        }

        public void SetChecksum(ulong checksum) => _checksum = checksum;

        public void SetCounts(int vertexCount, int triangleCount, int portalCount)
        {
            if ((uint)vertexCount > (uint)VertexCapacity)
            {
                throw new InvalidOperationException(
                    $"NavTile vertexCount {vertexCount} exceeds outputVertexCapacity {VertexCapacity}; required {vertexCount}.");
            }

            if ((uint)triangleCount > (uint)TriangleCapacity)
            {
                throw new InvalidOperationException(
                    $"NavTile triangleCount {triangleCount} exceeds outputTriangleCapacity {TriangleCapacity}; required {triangleCount}.");
            }

            if ((uint)portalCount > (uint)PortalCapacity)
            {
                throw new InvalidOperationException(
                    $"NavTile portalCount {portalCount} exceeds outputPortalCapacity {PortalCapacity}; required {portalCount}.");
            }

            _vertexCount = vertexCount;
            _triangleCount = triangleCount;
            _portalCount = portalCount;
        }

        public void ClearTopology()
        {
            _vertexCount = 0;
            _triangleCount = 0;
            _portalCount = 0;
            _checksum = 0UL;
        }

        public void CopyGeometryFrom(NavTile source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.VertexCount > VertexCapacity)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.outputVertexCapacity ({VertexCapacity}) exhausted; required {source.VertexCount}.");
            }

            if (source.TriangleCount > TriangleCapacity)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.outputTriangleCapacity ({TriangleCapacity}) exhausted; required {source.TriangleCount}.");
            }

            if (source.PortalCount > PortalCapacity)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.outputPortalCapacity ({PortalCapacity}) exhausted; required {source.PortalCount}.");
            }

            AssignHeader(source.TileId, source.TileVersion, source.BuildConfigHash, source.OriginXcm, source.OriginZcm);
            SetCounts(source.VertexCount, source.TriangleCount, source.PortalCount);
            if (source.VertexCount > 0)
            {
                Array.Copy(source.VertexXcm, VertexXcm, source.VertexCount);
                Array.Copy(source.VertexYcm, VertexYcm, source.VertexCount);
                Array.Copy(source.VertexZcm, VertexZcm, source.VertexCount);
            }

            if (source.TriangleCount > 0)
            {
                Array.Copy(source.TriA, TriA, source.TriangleCount);
                Array.Copy(source.TriB, TriB, source.TriangleCount);
                Array.Copy(source.TriC, TriC, source.TriangleCount);
                Array.Copy(source.N0, N0, source.TriangleCount);
                Array.Copy(source.N1, N1, source.TriangleCount);
                Array.Copy(source.N2, N2, source.TriangleCount);
                Array.Copy(source.TriAreaIds, TriAreaIds, source.TriangleCount);
            }

            if (source.PortalCount > 0)
            {
                Array.Copy(source.Portals, Portals, source.PortalCount);
            }

            _checksum = source.Checksum;
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
        public readonly int LeftYcm;
        public readonly int LeftZcm;
        public readonly int RightXcm;
        public readonly int RightYcm;
        public readonly int RightZcm;
        public readonly int ClearanceCm;

        public NavBorderPortal(
            NavPortalSide side,
            short u0,
            short v0,
            short u1,
            short v1,
            int leftXcm,
            int leftYcm,
            int leftZcm,
            int rightXcm,
            int rightYcm,
            int rightZcm,
            int clearanceCm)
        {
            Side = side;
            U0 = u0;
            V0 = v0;
            U1 = u1;
            V1 = v1;
            LeftXcm = leftXcm;
            LeftYcm = leftYcm;
            LeftZcm = leftZcm;
            RightXcm = rightXcm;
            RightYcm = rightYcm;
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
