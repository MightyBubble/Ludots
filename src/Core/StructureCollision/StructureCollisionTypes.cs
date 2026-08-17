using System;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.StructureCollision
{
    public enum StructureSurfaceKind : byte
    {
        Deck = 0,
        Ramp = 1,
        Platform = 2,
        Wall = 3,
        Gate = 4,
        Portal = 5
    }

    [Flags]
    public enum StructureSurfaceFlags : ushort
    {
        None = 0,
        Walkable = 1 << 0,
        BlocksMovement = 1 << 1,
        BlocksProjectiles = 1 << 2,
        BlocksVision = 1 << 3,
        PickingGround = 1 << 4,
        CameraGround = 1 << 5,
        Mutable = 1 << 6
    }

    public enum StructureShapeKind : byte
    {
        ConvexPrism = 0,
        OrientedBox = 1,
        Cylinder = 2,
        RampPlane = 3,
        WalkablePolygon = 4,
        WallSegment = 5,
        PortalLink = 6
    }

    [Flags]
    public enum GroundSurfaceHitMask : byte
    {
        None = 0,
        Terrain = 1 << 0,
        Structure = 1 << 1,
        Walkable = 1 << 2,
        Blocker = 1 << 3,
        Portal = 1 << 4
    }

    public enum StructureGroundSelectionMode : byte
    {
        HighestWithinBand = 0,
        ClosestToReferenceHeight = 1
    }

    public enum StructureCollisionBlockerKind : byte
    {
        Movement = 0,
        Projectile = 1,
        Vision = 2
    }

    public static class GroundSurfaceIds
    {
        public const int NoSurface = -1;
        public const int TerrainSurface = 0;
        public const int TerrainLayer = -1;
    }

    public readonly struct StructureCollisionHeader
    {
        public StructureCollisionHeader(int version, WorldAabbCm worldBounds, int chunkSizeCm, int revision, float coordinateScale)
        {
            if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
            if (chunkSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSizeCm));
            if (worldBounds.Width <= 0 || worldBounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(worldBounds));
            if (!float.IsFinite(coordinateScale) || coordinateScale <= 0f) throw new ArgumentOutOfRangeException(nameof(coordinateScale));

            Version = version;
            WorldBounds = worldBounds;
            ChunkSizeCm = chunkSizeCm;
            Revision = revision;
            CoordinateScale = coordinateScale;
        }

        public int Version { get; }

        public WorldAabbCm WorldBounds { get; }

        public int ChunkSizeCm { get; }

        public int Revision { get; }

        public float CoordinateScale { get; }
    }

    public readonly struct StructureLayerDefinition
    {
        public StructureLayerDefinition(string id, int value)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Layer id must not be empty.", nameof(id));

            Id = id;
            Value = value;
        }

        public string Id { get; }

        public int Value { get; }
    }

    public readonly struct StructureAgentMaskDefinition
    {
        public StructureAgentMaskDefinition(string id, uint bits)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Agent mask id must not be empty.", nameof(id));
            if (bits == 0) throw new ArgumentOutOfRangeException(nameof(bits));

            Id = id;
            Bits = bits;
        }

        public string Id { get; }

        public uint Bits { get; }
    }

    public readonly struct GroundSurfaceQueryPolicy
    {
        public GroundSurfaceQueryPolicy(
            int layerId = -1,
            uint agentMask = uint.MaxValue,
            float minHeightCm = float.NegativeInfinity,
            float maxHeightCm = float.PositiveInfinity,
            float maxSlopeDegrees = 90f,
            bool walkableOnly = true,
            StructureGroundSelectionMode selectionMode = StructureGroundSelectionMode.HighestWithinBand,
            float referenceHeightCm = float.NaN)
        {
            if (agentMask == 0) throw new ArgumentOutOfRangeException(nameof(agentMask));
            if (!float.IsFinite(maxSlopeDegrees) || maxSlopeDegrees < 0f || maxSlopeDegrees > 90f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSlopeDegrees));
            }

            LayerId = layerId;
            AgentMask = agentMask;
            MinHeightCm = minHeightCm;
            MaxHeightCm = maxHeightCm;
            MaxSlopeDegrees = maxSlopeDegrees;
            WalkableOnly = walkableOnly;
            SelectionMode = selectionMode;
            ReferenceHeightCm = referenceHeightCm;
        }

        public int LayerId { get; }

        public uint AgentMask { get; }

        public float MinHeightCm { get; }

        public float MaxHeightCm { get; }

        public float MaxSlopeDegrees { get; }

        public bool WalkableOnly { get; }

        public StructureGroundSelectionMode SelectionMode { get; }

        public float ReferenceHeightCm { get; }

        public static GroundSurfaceQueryPolicy Default { get; } = new GroundSurfaceQueryPolicy();

        public bool AllowsLayer(int layerId) => LayerId < 0 || LayerId == layerId;

        public bool AllowsAgent(uint surfaceAgentMask) => (surfaceAgentMask & AgentMask) != 0;

        public bool AllowsHeight(float heightCm)
            => heightCm >= MinHeightCm && heightCm <= MaxHeightCm;
    }

    public sealed class StructureGroundingDiagnostics
    {
        public int TotalSurfaces { get; internal set; }
        public int LoadedChunks { get; internal set; }
        public int SampledPoints { get; private set; }
        public int VisitedChunks { get; private set; }
        public int TestedCandidateSurfaces { get; private set; }
        public int MaxCandidateSurfacesPerSample { get; private set; }

        public void ResetCounters()
        {
            SampledPoints = 0;
            VisitedChunks = 0;
            TestedCandidateSurfaces = 0;
            MaxCandidateSurfacesPerSample = 0;
        }

        internal void RecordSample(int candidateCount, bool visitedChunk)
        {
            SampledPoints++;
            if (visitedChunk)
            {
                VisitedChunks++;
            }

            TestedCandidateSurfaces += candidateCount;
            if (candidateCount > MaxCandidateSurfacesPerSample)
            {
                MaxCandidateSurfacesPerSample = candidateCount;
            }
        }
    }

    public readonly struct StructureChunkRevision
    {
        public StructureChunkRevision(int chunkIndex, int revision)
        {
            ChunkIndex = chunkIndex;
            Revision = revision;
        }

        public int ChunkIndex { get; }

        public int Revision { get; }
    }

    public readonly struct StructureCollisionBlockerView
    {
        public StructureCollisionBlockerView(
            int surfaceId,
            int layerId,
            uint agentMask,
            WorldAabbCm bounds,
            StructureSurfaceFlags flags,
            int shapeRef,
            int sourceChunkIndex)
        {
            SurfaceId = surfaceId;
            LayerId = layerId;
            AgentMask = agentMask;
            Bounds = bounds;
            Flags = flags;
            ShapeRef = shapeRef;
            SourceChunkIndex = sourceChunkIndex;
        }

        public int SurfaceId { get; }

        public int LayerId { get; }

        public uint AgentMask { get; }

        public WorldAabbCm Bounds { get; }

        public StructureSurfaceFlags Flags { get; }

        public int ShapeRef { get; }

        public int SourceChunkIndex { get; }
    }

    public readonly struct StructureCollisionDebugRecord
    {
        public StructureCollisionDebugRecord(
            int surfaceId,
            int layerId,
            uint agentMask,
            int sourceChunkIndex,
            float selectedHeightCm,
            StructureSurfaceFlags flags)
        {
            SurfaceId = surfaceId;
            LayerId = layerId;
            AgentMask = agentMask;
            SourceChunkIndex = sourceChunkIndex;
            SelectedHeightCm = selectedHeightCm;
            Flags = flags;
        }

        public int SurfaceId { get; }

        public int LayerId { get; }

        public uint AgentMask { get; }

        public int SourceChunkIndex { get; }

        public float SelectedHeightCm { get; }

        public StructureSurfaceFlags Flags { get; }
    }
}
