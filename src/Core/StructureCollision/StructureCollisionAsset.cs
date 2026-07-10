using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics;

namespace Ludots.Core.StructureCollision
{
    public readonly struct StructureChunkIndexEntry
    {
        public StructureChunkIndexEntry(
            int surfaceStart,
            int surfaceCount,
            int blockerStart,
            int blockerCount,
            int portalStart,
            int portalCount)
        {
            SurfaceStart = surfaceStart;
            SurfaceCount = surfaceCount;
            BlockerStart = blockerStart;
            BlockerCount = blockerCount;
            PortalStart = portalStart;
            PortalCount = portalCount;
        }

        public int SurfaceStart { get; }

        public int SurfaceCount { get; }

        public int BlockerStart { get; }

        public int BlockerCount { get; }

        public int PortalStart { get; }

        public int PortalCount { get; }
    }

    public sealed class StructureSurfaceSoA
    {
        public StructureSurfaceSoA(
            int[] surfaceIds,
            StructureSurfaceKind[] kinds,
            StructureSurfaceFlags[] flags,
            int[] layerIds,
            uint[] agentMasks,
            WorldAabbCm[] bounds,
            float[] minHeightCm,
            float[] maxHeightCm,
            float[] normalX,
            float[] normalY,
            float[] normalZ,
            float[] slopeDegrees,
            int[] shapeRefs,
            int[] sourcePrefabIds,
            int[] sourcePartIds)
        {
            Count = surfaceIds?.Length ?? throw new ArgumentNullException(nameof(surfaceIds));
            RequireLength(kinds, Count, nameof(kinds));
            RequireLength(flags, Count, nameof(flags));
            RequireLength(layerIds, Count, nameof(layerIds));
            RequireLength(agentMasks, Count, nameof(agentMasks));
            RequireLength(bounds, Count, nameof(bounds));
            RequireLength(minHeightCm, Count, nameof(minHeightCm));
            RequireLength(maxHeightCm, Count, nameof(maxHeightCm));
            RequireLength(normalX, Count, nameof(normalX));
            RequireLength(normalY, Count, nameof(normalY));
            RequireLength(normalZ, Count, nameof(normalZ));
            RequireLength(slopeDegrees, Count, nameof(slopeDegrees));
            RequireLength(shapeRefs, Count, nameof(shapeRefs));
            RequireLength(sourcePrefabIds, Count, nameof(sourcePrefabIds));
            RequireLength(sourcePartIds, Count, nameof(sourcePartIds));

            SurfaceIds = surfaceIds;
            Kinds = kinds;
            Flags = flags;
            LayerIds = layerIds;
            AgentMasks = agentMasks;
            Bounds = bounds;
            MinHeightCm = minHeightCm;
            MaxHeightCm = maxHeightCm;
            NormalX = normalX;
            NormalY = normalY;
            NormalZ = normalZ;
            SlopeDegrees = slopeDegrees;
            ShapeRefs = shapeRefs;
            SourcePrefabIds = sourcePrefabIds;
            SourcePartIds = sourcePartIds;
        }

        public int Count { get; }

        public int[] SurfaceIds { get; }

        public StructureSurfaceKind[] Kinds { get; }

        public StructureSurfaceFlags[] Flags { get; }

        public int[] LayerIds { get; }

        public uint[] AgentMasks { get; }

        public WorldAabbCm[] Bounds { get; }

        public float[] MinHeightCm { get; }

        public float[] MaxHeightCm { get; }

        public float[] NormalX { get; }

        public float[] NormalY { get; }

        public float[] NormalZ { get; }

        public float[] SlopeDegrees { get; }

        public int[] ShapeRefs { get; }

        public int[] SourcePrefabIds { get; }

        public int[] SourcePartIds { get; }

        private static void RequireLength<T>(T[]? values, int expected, string name)
        {
            if (values == null) throw new ArgumentNullException(name);
            if (values.Length != expected)
            {
                throw new ArgumentException($"Structure surface SoA '{name}' expected length {expected}, got {values.Length}.", name);
            }
        }
    }

    public sealed class StructureShapeSoA
    {
        public StructureShapeSoA(
            StructureShapeKind[] kinds,
            WorldAabbCm[] bounds,
            float[] minHeightCm,
            float[] maxHeightCm,
            float[] planeOriginXCm,
            float[] planeOriginZCm,
            float[] planeHeightCm,
            float[] planeSlopeX,
            float[] planeSlopeZ,
            float[] normalX,
            float[] normalY,
            float[] normalZ,
            int[] vertexStart,
            int[] vertexCount,
            float[] vertexXCm,
            float[] vertexZCm,
            float[] centerXCm,
            float[] centerZCm,
            float[] halfWidthCm,
            float[] halfDepthCm,
            float[] yawRadians,
            float[] radiusCm,
            float[] segmentAXCm,
            float[] segmentAZCm,
            float[] segmentBXCm,
            float[] segmentBZCm,
            float[] segmentHalfWidthCm)
        {
            Count = kinds?.Length ?? throw new ArgumentNullException(nameof(kinds));
            RequireLength(bounds, Count, nameof(bounds));
            RequireLength(minHeightCm, Count, nameof(minHeightCm));
            RequireLength(maxHeightCm, Count, nameof(maxHeightCm));
            RequireLength(planeOriginXCm, Count, nameof(planeOriginXCm));
            RequireLength(planeOriginZCm, Count, nameof(planeOriginZCm));
            RequireLength(planeHeightCm, Count, nameof(planeHeightCm));
            RequireLength(planeSlopeX, Count, nameof(planeSlopeX));
            RequireLength(planeSlopeZ, Count, nameof(planeSlopeZ));
            RequireLength(normalX, Count, nameof(normalX));
            RequireLength(normalY, Count, nameof(normalY));
            RequireLength(normalZ, Count, nameof(normalZ));
            RequireLength(vertexStart, Count, nameof(vertexStart));
            RequireLength(vertexCount, Count, nameof(vertexCount));
            RequireLength(centerXCm, Count, nameof(centerXCm));
            RequireLength(centerZCm, Count, nameof(centerZCm));
            RequireLength(halfWidthCm, Count, nameof(halfWidthCm));
            RequireLength(halfDepthCm, Count, nameof(halfDepthCm));
            RequireLength(yawRadians, Count, nameof(yawRadians));
            RequireLength(radiusCm, Count, nameof(radiusCm));
            RequireLength(segmentAXCm, Count, nameof(segmentAXCm));
            RequireLength(segmentAZCm, Count, nameof(segmentAZCm));
            RequireLength(segmentBXCm, Count, nameof(segmentBXCm));
            RequireLength(segmentBZCm, Count, nameof(segmentBZCm));
            RequireLength(segmentHalfWidthCm, Count, nameof(segmentHalfWidthCm));

            Kinds = kinds;
            Bounds = bounds;
            MinHeightCm = minHeightCm;
            MaxHeightCm = maxHeightCm;
            PlaneOriginXCm = planeOriginXCm;
            PlaneOriginZCm = planeOriginZCm;
            PlaneHeightCm = planeHeightCm;
            PlaneSlopeX = planeSlopeX;
            PlaneSlopeZ = planeSlopeZ;
            NormalX = normalX;
            NormalY = normalY;
            NormalZ = normalZ;
            VertexStart = vertexStart;
            VertexCount = vertexCount;
            VertexXCm = vertexXCm ?? throw new ArgumentNullException(nameof(vertexXCm));
            VertexZCm = vertexZCm ?? throw new ArgumentNullException(nameof(vertexZCm));
            CenterXCm = centerXCm;
            CenterZCm = centerZCm;
            HalfWidthCm = halfWidthCm;
            HalfDepthCm = halfDepthCm;
            YawRadians = yawRadians;
            RadiusCm = radiusCm;
            SegmentAXCm = segmentAXCm;
            SegmentAZCm = segmentAZCm;
            SegmentBXCm = segmentBXCm;
            SegmentBZCm = segmentBZCm;
            SegmentHalfWidthCm = segmentHalfWidthCm;
        }

        public int Count { get; }

        public StructureShapeKind[] Kinds { get; }

        public WorldAabbCm[] Bounds { get; }

        public float[] MinHeightCm { get; }

        public float[] MaxHeightCm { get; }

        public float[] PlaneOriginXCm { get; }

        public float[] PlaneOriginZCm { get; }

        public float[] PlaneHeightCm { get; }

        public float[] PlaneSlopeX { get; }

        public float[] PlaneSlopeZ { get; }

        public float[] NormalX { get; }

        public float[] NormalY { get; }

        public float[] NormalZ { get; }

        public int[] VertexStart { get; }

        public int[] VertexCount { get; }

        public float[] VertexXCm { get; }

        public float[] VertexZCm { get; }

        public float[] CenterXCm { get; }

        public float[] CenterZCm { get; }

        public float[] HalfWidthCm { get; }

        public float[] HalfDepthCm { get; }

        public float[] YawRadians { get; }

        public float[] RadiusCm { get; }

        public float[] SegmentAXCm { get; }

        public float[] SegmentAZCm { get; }

        public float[] SegmentBXCm { get; }

        public float[] SegmentBZCm { get; }

        public float[] SegmentHalfWidthCm { get; }

        private static void RequireLength<T>(T[]? values, int expected, string name)
        {
            if (values == null) throw new ArgumentNullException(name);
            if (values.Length != expected)
            {
                throw new ArgumentException($"Structure shape SoA '{name}' expected length {expected}, got {values.Length}.", name);
            }
        }
    }

    public sealed class StructureCollisionAsset
    {
        private readonly Dictionary<int, int> _surfaceIndexById;

        public StructureCollisionAsset(
            StructureCollisionHeader header,
            StructureLayerDefinition[] layers,
            StructureAgentMaskDefinition[] agentMasks,
            StructureSurfaceSoA surfaces,
            StructureShapeSoA shapes,
            StructureChunkIndexEntry[] chunks,
            int[] chunkSurfaceIndices,
            int[] chunkBlockerIndices,
            int[] chunkPortalIndices,
            int[] surfaceChunkStart,
            int[] surfaceChunkCount,
            int[] surfaceChunkIndices,
            int chunkColumns,
            int chunkRows)
        {
            Header = header;
            Layers = layers ?? throw new ArgumentNullException(nameof(layers));
            AgentMasks = agentMasks ?? throw new ArgumentNullException(nameof(agentMasks));
            Surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
            Shapes = shapes ?? throw new ArgumentNullException(nameof(shapes));
            Chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
            ChunkSurfaceIndices = chunkSurfaceIndices ?? throw new ArgumentNullException(nameof(chunkSurfaceIndices));
            ChunkBlockerIndices = chunkBlockerIndices ?? throw new ArgumentNullException(nameof(chunkBlockerIndices));
            ChunkPortalIndices = chunkPortalIndices ?? throw new ArgumentNullException(nameof(chunkPortalIndices));
            SurfaceChunkStart = surfaceChunkStart ?? throw new ArgumentNullException(nameof(surfaceChunkStart));
            SurfaceChunkCount = surfaceChunkCount ?? throw new ArgumentNullException(nameof(surfaceChunkCount));
            SurfaceChunkIndices = surfaceChunkIndices ?? throw new ArgumentNullException(nameof(surfaceChunkIndices));
            ChunkColumns = chunkColumns > 0 ? chunkColumns : throw new ArgumentOutOfRangeException(nameof(chunkColumns));
            ChunkRows = chunkRows > 0 ? chunkRows : throw new ArgumentOutOfRangeException(nameof(chunkRows));

            if (Chunks.Length != checked(ChunkColumns * ChunkRows))
            {
                throw new ArgumentException("Chunk index length must match chunk grid.");
            }

            ValidateInternalReferences();

            _surfaceIndexById = new Dictionary<int, int>(Surfaces.Count);
            for (int i = 0; i < Surfaces.Count; i++)
            {
                if (!_surfaceIndexById.TryAdd(Surfaces.SurfaceIds[i], i))
                {
                    throw new InvalidOperationException($"Duplicate structure surface id '{Surfaces.SurfaceIds[i]}'.");
                }
            }
        }

        public StructureCollisionHeader Header { get; }

        public StructureLayerDefinition[] Layers { get; }

        public StructureAgentMaskDefinition[] AgentMasks { get; }

        public StructureSurfaceSoA Surfaces { get; }

        public StructureShapeSoA Shapes { get; }

        public StructureChunkIndexEntry[] Chunks { get; }

        public int[] ChunkSurfaceIndices { get; }

        public int[] ChunkBlockerIndices { get; }

        public int[] ChunkPortalIndices { get; }

        public int[] SurfaceChunkStart { get; }

        public int[] SurfaceChunkCount { get; }

        public int[] SurfaceChunkIndices { get; }

        public int ChunkColumns { get; }

        public int ChunkRows { get; }

        public int SurfaceCount => Surfaces.Count;

        public int ShapeCount => Shapes.Count;

        public int ChunkCount => Chunks.Length;

        private void ValidateInternalReferences()
        {
            if (SurfaceChunkStart.Length != Surfaces.Count)
            {
                throw new InvalidOperationException("Structure surface chunk start table length must match surface count.");
            }

            if (SurfaceChunkCount.Length != Surfaces.Count)
            {
                throw new InvalidOperationException("Structure surface chunk count table length must match surface count.");
            }

            for (int surfaceIndex = 0; surfaceIndex < Surfaces.Count; surfaceIndex++)
            {
                int shapeRef = Surfaces.ShapeRefs[surfaceIndex];
                if ((uint)shapeRef >= (uint)Shapes.Count)
                {
                    throw new InvalidOperationException(
                        $"Structure surface '{Surfaces.SurfaceIds[surfaceIndex]}' references out-of-range shape ref '{shapeRef}'.");
                }

                ValidateSpan(
                    "surface chunk",
                    surfaceIndex,
                    SurfaceChunkStart[surfaceIndex],
                    SurfaceChunkCount[surfaceIndex],
                    SurfaceChunkIndices.Length);
                ValidateIndexRange(
                    "surface chunk",
                    surfaceIndex,
                    SurfaceChunkIndices,
                    SurfaceChunkStart[surfaceIndex],
                    SurfaceChunkCount[surfaceIndex],
                    Chunks.Length);
            }

            for (int chunkIndex = 0; chunkIndex < Chunks.Length; chunkIndex++)
            {
                StructureChunkIndexEntry chunk = Chunks[chunkIndex];
                ValidateSpan("chunk surface", chunkIndex, chunk.SurfaceStart, chunk.SurfaceCount, ChunkSurfaceIndices.Length);
                ValidateSpan("chunk blocker", chunkIndex, chunk.BlockerStart, chunk.BlockerCount, ChunkBlockerIndices.Length);
                ValidateSpan("chunk portal", chunkIndex, chunk.PortalStart, chunk.PortalCount, ChunkPortalIndices.Length);
                ValidateIndexRange("chunk surface", chunkIndex, ChunkSurfaceIndices, chunk.SurfaceStart, chunk.SurfaceCount, Surfaces.Count);
                ValidateIndexRange("chunk blocker", chunkIndex, ChunkBlockerIndices, chunk.BlockerStart, chunk.BlockerCount, Surfaces.Count);
                ValidateIndexRange("chunk portal", chunkIndex, ChunkPortalIndices, chunk.PortalStart, chunk.PortalCount, Surfaces.Count);
            }
        }

        private static void ValidateSpan(string spanName, int ownerIndex, int start, int count, int length)
        {
            if (start < 0 || count < 0 || start > length || count > length - start)
            {
                throw new InvalidOperationException(
                    $"Structure {spanName} span for index '{ownerIndex}' is out of range (start={start}, count={count}, length={length}).");
            }
        }

        private static void ValidateIndexRange(
            string spanName,
            int ownerIndex,
            int[] indices,
            int start,
            int count,
            int exclusiveLimit)
        {
            for (int i = 0; i < count; i++)
            {
                int value = indices[start + i];
                if ((uint)value >= (uint)exclusiveLimit)
                {
                    throw new InvalidOperationException(
                        $"Structure {spanName} span for index '{ownerIndex}' contains out-of-range reference '{value}'.");
                }
            }
        }

        public bool TryGetSurfaceIndexById(int surfaceId, out int surfaceIndex)
            => _surfaceIndexById.TryGetValue(surfaceId, out surfaceIndex);

        public bool TryGetChunkIndex(float worldXCm, float worldZCm, out int chunkIndex)
        {
            chunkIndex = -1;
            WorldAabbCm bounds = Header.WorldBounds;
            if (!float.IsFinite(worldXCm) ||
                !float.IsFinite(worldZCm) ||
                worldXCm < bounds.Left ||
                worldXCm >= bounds.Right ||
                worldZCm < bounds.Top ||
                worldZCm >= bounds.Bottom)
            {
                return false;
            }

            int chunkX = (int)((worldXCm - bounds.Left) / Header.ChunkSizeCm);
            int chunkZ = (int)((worldZCm - bounds.Top) / Header.ChunkSizeCm);
            chunkX = Math.Clamp(chunkX, 0, ChunkColumns - 1);
            chunkZ = Math.Clamp(chunkZ, 0, ChunkRows - 1);
            chunkIndex = GetChunkIndex(chunkX, chunkZ);
            return true;
        }

        public int GetChunkIndex(int chunkX, int chunkZ)
        {
            if ((uint)chunkX >= (uint)ChunkColumns) throw new ArgumentOutOfRangeException(nameof(chunkX));
            if ((uint)chunkZ >= (uint)ChunkRows) throw new ArgumentOutOfRangeException(nameof(chunkZ));

            return chunkZ * ChunkColumns + chunkX;
        }

        public bool TryEvaluateSurfaceHeight(int surfaceIndex, float worldXCm, float worldZCm, out float heightCm)
        {
            heightCm = default;
            if ((uint)surfaceIndex >= (uint)Surfaces.Count)
            {
                return false;
            }

            int shapeIndex = Surfaces.ShapeRefs[surfaceIndex];
            if (!TryContainsShapePoint(shapeIndex, worldXCm, worldZCm))
            {
                return false;
            }

            heightCm = EvaluateShapeHeight(shapeIndex, worldXCm, worldZCm);
            return float.IsFinite(heightCm) &&
                   heightCm >= Surfaces.MinHeightCm[surfaceIndex] &&
                   heightCm <= Surfaces.MaxHeightCm[surfaceIndex];
        }

        public int GetPrimaryChunkForSurface(int surfaceIndex)
        {
            if ((uint)surfaceIndex >= (uint)SurfaceChunkStart.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(surfaceIndex));
            }

            int count = SurfaceChunkCount[surfaceIndex];
            return count > 0 ? SurfaceChunkIndices[SurfaceChunkStart[surfaceIndex]] : -1;
        }

        private bool TryContainsShapePoint(int shapeIndex, float worldXCm, float worldZCm)
        {
            if ((uint)shapeIndex >= (uint)Shapes.Count)
            {
                return false;
            }

            WorldAabbCm bounds = Shapes.Bounds[shapeIndex];
            if (!Contains(in bounds, worldXCm, worldZCm))
            {
                return false;
            }

            switch (Shapes.Kinds[shapeIndex])
            {
                case StructureShapeKind.WalkablePolygon:
                case StructureShapeKind.RampPlane:
                case StructureShapeKind.ConvexPrism:
                case StructureShapeKind.PortalLink:
                    return ContainsPolygon(shapeIndex, worldXCm, worldZCm);
                case StructureShapeKind.OrientedBox:
                    return ContainsOrientedBox(shapeIndex, worldXCm, worldZCm);
                case StructureShapeKind.Cylinder:
                    return ContainsCylinder(shapeIndex, worldXCm, worldZCm);
                case StructureShapeKind.WallSegment:
                    return ContainsSegmentCapsule(shapeIndex, worldXCm, worldZCm);
                default:
                    return false;
            }
        }

        private float EvaluateShapeHeight(int shapeIndex, float worldXCm, float worldZCm)
        {
            StructureShapeKind kind = Shapes.Kinds[shapeIndex];
            if (kind == StructureShapeKind.OrientedBox ||
                kind == StructureShapeKind.Cylinder ||
                kind == StructureShapeKind.WallSegment)
            {
                return Shapes.MaxHeightCm[shapeIndex];
            }

            return Shapes.PlaneHeightCm[shapeIndex] +
                   ((worldXCm - Shapes.PlaneOriginXCm[shapeIndex]) * Shapes.PlaneSlopeX[shapeIndex]) +
                   ((worldZCm - Shapes.PlaneOriginZCm[shapeIndex]) * Shapes.PlaneSlopeZ[shapeIndex]);
        }

        private bool ContainsPolygon(int shapeIndex, float worldXCm, float worldZCm)
        {
            int count = Shapes.VertexCount[shapeIndex];
            if (count < 3)
            {
                return Contains(in Shapes.Bounds[shapeIndex], worldXCm, worldZCm);
            }

            int start = Shapes.VertexStart[shapeIndex];
            bool hasPositive = false;
            bool hasNegative = false;
            for (int i = 0; i < count; i++)
            {
                int j = i + 1 == count ? 0 : i + 1;
                float ax = Shapes.VertexXCm[start + i];
                float az = Shapes.VertexZCm[start + i];
                float bx = Shapes.VertexXCm[start + j];
                float bz = Shapes.VertexZCm[start + j];
                float cross = ((bx - ax) * (worldZCm - az)) - ((bz - az) * (worldXCm - ax));
                if (cross > 0.001f) hasPositive = true;
                else if (cross < -0.001f) hasNegative = true;
                if (hasPositive && hasNegative)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ContainsOrientedBox(int shapeIndex, float worldXCm, float worldZCm)
        {
            float dx = worldXCm - Shapes.CenterXCm[shapeIndex];
            float dz = worldZCm - Shapes.CenterZCm[shapeIndex];
            float yaw = -Shapes.YawRadians[shapeIndex];
            float c = MathF.Cos(yaw);
            float s = MathF.Sin(yaw);
            float localX = (dx * c) - (dz * s);
            float localZ = (dx * s) + (dz * c);
            return MathF.Abs(localX) <= Shapes.HalfWidthCm[shapeIndex] + 0.001f &&
                   MathF.Abs(localZ) <= Shapes.HalfDepthCm[shapeIndex] + 0.001f;
        }

        private bool ContainsCylinder(int shapeIndex, float worldXCm, float worldZCm)
        {
            float dx = worldXCm - Shapes.CenterXCm[shapeIndex];
            float dz = worldZCm - Shapes.CenterZCm[shapeIndex];
            float radius = Shapes.RadiusCm[shapeIndex];
            return (dx * dx) + (dz * dz) <= (radius * radius) + 0.001f;
        }

        private bool ContainsSegmentCapsule(int shapeIndex, float worldXCm, float worldZCm)
        {
            float ax = Shapes.SegmentAXCm[shapeIndex];
            float az = Shapes.SegmentAZCm[shapeIndex];
            float bx = Shapes.SegmentBXCm[shapeIndex];
            float bz = Shapes.SegmentBZCm[shapeIndex];
            float abx = bx - ax;
            float abz = bz - az;
            float lenSq = (abx * abx) + (abz * abz);
            float t = lenSq <= 0.001f
                ? 0f
                : Math.Clamp((((worldXCm - ax) * abx) + ((worldZCm - az) * abz)) / lenSq, 0f, 1f);
            float px = ax + (abx * t);
            float pz = az + (abz * t);
            float dx = worldXCm - px;
            float dz = worldZCm - pz;
            float halfWidth = Shapes.SegmentHalfWidthCm[shapeIndex];
            return (dx * dx) + (dz * dz) <= (halfWidth * halfWidth) + 0.001f;
        }

        private static bool Contains(in WorldAabbCm bounds, float worldXCm, float worldZCm)
            => worldXCm >= bounds.Left &&
               worldXCm < bounds.Right &&
               worldZCm >= bounds.Top &&
               worldZCm < bounds.Bottom;
    }

    public sealed class StructureDirtyChunkState
    {
        private readonly int[] _revisions;
        private readonly byte[] _dirty;

        public StructureDirtyChunkState(int chunkCount)
        {
            if (chunkCount < 0) throw new ArgumentOutOfRangeException(nameof(chunkCount));
            _revisions = new int[chunkCount];
            _dirty = new byte[chunkCount];
        }

        public int ChunkCount => _revisions.Length;

        public int GetRevision(int chunkIndex)
        {
            if ((uint)chunkIndex >= (uint)_revisions.Length) throw new ArgumentOutOfRangeException(nameof(chunkIndex));
            return _revisions[chunkIndex];
        }

        public bool IsDirty(int chunkIndex)
        {
            if ((uint)chunkIndex >= (uint)_dirty.Length) throw new ArgumentOutOfRangeException(nameof(chunkIndex));
            return _dirty[chunkIndex] != 0;
        }

        public int CopyDirtyChunks(Span<StructureChunkRevision> output)
        {
            int required = CountDirtyChunks();
            if (output.Length < required)
            {
                throw new InvalidOperationException(
                    $"Structure dirty chunk output span too small: required {required}, got {output.Length}.");
            }

            int written = 0;
            for (int i = 0; i < _dirty.Length; i++)
            {
                if (_dirty[i] == 0)
                {
                    continue;
                }

                output[written++] = new StructureChunkRevision(i, _revisions[i]);
            }

            return written;
        }

        public int CountDirtyChunks()
        {
            int count = 0;
            for (int i = 0; i < _dirty.Length; i++)
            {
                if (_dirty[i] != 0)
                {
                    count++;
                }
            }

            return count;
        }

        public void ClearDirty()
        {
            Array.Clear(_dirty, 0, _dirty.Length);
        }

        internal void MarkDirty(int chunkIndex)
        {
            if ((uint)chunkIndex >= (uint)_revisions.Length) throw new ArgumentOutOfRangeException(nameof(chunkIndex));
            _revisions[chunkIndex] = checked(_revisions[chunkIndex] + 1);
            _dirty[chunkIndex] = 1;
        }
    }

    public sealed class StructureCollisionRuntimeState
    {
        private readonly byte[] _surfaceEnabled;

        public StructureCollisionRuntimeState(StructureCollisionAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            _surfaceEnabled = new byte[asset.SurfaceCount];
            Array.Fill(_surfaceEnabled, (byte)1);
            DirtyChunks = new StructureDirtyChunkState(asset.ChunkCount);
        }

        public StructureDirtyChunkState DirtyChunks { get; }

        public bool IsSurfaceEnabled(int surfaceIndex)
        {
            if ((uint)surfaceIndex >= (uint)_surfaceEnabled.Length) return false;
            return _surfaceEnabled[surfaceIndex] != 0;
        }

        public bool SetSurfaceEnabled(StructureCollisionAsset asset, int surfaceIndex, bool enabled)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if ((uint)surfaceIndex >= (uint)_surfaceEnabled.Length) throw new ArgumentOutOfRangeException(nameof(surfaceIndex));

            byte newValue = (byte)(enabled ? 1 : 0);
            if (_surfaceEnabled[surfaceIndex] == newValue)
            {
                return false;
            }

            _surfaceEnabled[surfaceIndex] = newValue;
            int start = asset.SurfaceChunkStart[surfaceIndex];
            int count = asset.SurfaceChunkCount[surfaceIndex];
            for (int i = 0; i < count; i++)
            {
                DirtyChunks.MarkDirty(asset.SurfaceChunkIndices[start + i]);
            }

            return true;
        }
    }
}
