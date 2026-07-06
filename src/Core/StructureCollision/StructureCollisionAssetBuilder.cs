using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics;

namespace Ludots.Core.StructureCollision
{
    public readonly struct StructurePointCm
    {
        public StructurePointCm(float xCm, float zCm)
        {
            Xcm = xCm;
            Zcm = zCm;
        }

        public float Xcm { get; }

        public float Zcm { get; }
    }

    public sealed class StructureShapeDefinition
    {
        public string Id { get; set; } = string.Empty;
        public StructureShapeKind Kind { get; set; }
        public WorldAabbCm Bounds { get; set; }
        public StructurePointCm[] Vertices { get; set; } = Array.Empty<StructurePointCm>();
        public float MinHeightCm { get; set; }
        public float MaxHeightCm { get; set; }
        public float PlaneOriginXCm { get; set; }
        public float PlaneOriginZCm { get; set; }
        public float PlaneHeightCm { get; set; }
        public float PlaneSlopeX { get; set; }
        public float PlaneSlopeZ { get; set; }
        public float CenterXCm { get; set; }
        public float CenterZCm { get; set; }
        public float HalfWidthCm { get; set; }
        public float HalfDepthCm { get; set; }
        public float YawRadians { get; set; }
        public float RadiusCm { get; set; }
        public float SegmentAXCm { get; set; }
        public float SegmentAZCm { get; set; }
        public float SegmentBXCm { get; set; }
        public float SegmentBZCm { get; set; }
        public float SegmentHalfWidthCm { get; set; }
    }

    public sealed class StructureSurfaceDefinition
    {
        public int SurfaceId { get; set; }
        public StructureSurfaceKind Kind { get; set; }
        public StructureSurfaceFlags Flags { get; set; }
        public int LayerId { get; set; }
        public uint AgentMask { get; set; }
        public string ShapeId { get; set; } = string.Empty;
        public WorldAabbCm? Bounds { get; set; }
        public float? MinHeightCm { get; set; }
        public float? MaxHeightCm { get; set; }
        public int SourcePrefabId { get; set; }
        public int SourcePartId { get; set; }
    }

    public static class StructureCollisionAssetBuilder
    {
        private sealed class ChunkBuildLists
        {
            public List<int>? Surfaces;
            public List<int>? Blockers;
            public List<int>? Portals;
        }

        public static StructureCollisionAsset Build(
            StructureCollisionHeader header,
            IReadOnlyList<StructureLayerDefinition> layers,
            IReadOnlyList<StructureAgentMaskDefinition> agentMasks,
            IReadOnlyList<StructureShapeDefinition> shapeDefinitions,
            IReadOnlyList<StructureSurfaceDefinition> surfaceDefinitions)
        {
            if (layers == null) throw new ArgumentNullException(nameof(layers));
            if (agentMasks == null) throw new ArgumentNullException(nameof(agentMasks));
            if (shapeDefinitions == null) throw new ArgumentNullException(nameof(shapeDefinitions));
            if (surfaceDefinitions == null) throw new ArgumentNullException(nameof(surfaceDefinitions));
            if (shapeDefinitions.Count == 0) throw new InvalidOperationException("Structure collision asset requires at least one shape.");

            var shapeIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
            var vertexX = new List<float>();
            var vertexZ = new List<float>();
            int shapeCount = shapeDefinitions.Count;
            var shapeKinds = new StructureShapeKind[shapeCount];
            var shapeBounds = new WorldAabbCm[shapeCount];
            var shapeMinHeight = new float[shapeCount];
            var shapeMaxHeight = new float[shapeCount];
            var planeOriginX = new float[shapeCount];
            var planeOriginZ = new float[shapeCount];
            var planeHeight = new float[shapeCount];
            var planeSlopeX = new float[shapeCount];
            var planeSlopeZ = new float[shapeCount];
            var normalX = new float[shapeCount];
            var normalY = new float[shapeCount];
            var normalZ = new float[shapeCount];
            var vertexStart = new int[shapeCount];
            var vertexCount = new int[shapeCount];
            var centerX = new float[shapeCount];
            var centerZ = new float[shapeCount];
            var halfWidth = new float[shapeCount];
            var halfDepth = new float[shapeCount];
            var yaw = new float[shapeCount];
            var radius = new float[shapeCount];
            var segmentAX = new float[shapeCount];
            var segmentAZ = new float[shapeCount];
            var segmentBX = new float[shapeCount];
            var segmentBZ = new float[shapeCount];
            var segmentHalfWidth = new float[shapeCount];

            for (int i = 0; i < shapeDefinitions.Count; i++)
            {
                StructureShapeDefinition definition = shapeDefinitions[i] ?? throw new InvalidOperationException($"Structure shape at index {i} is null.");
                if (string.IsNullOrWhiteSpace(definition.Id))
                {
                    throw new InvalidOperationException($"Structure shape at index {i} has no id.");
                }

                if (!shapeIndexById.TryAdd(definition.Id, i))
                {
                    throw new InvalidOperationException($"Duplicate structure shape id '{definition.Id}'.");
                }

                ValidateShape(definition);
                shapeKinds[i] = definition.Kind;
                shapeBounds[i] = ResolveShapeBounds(definition);
                shapeMinHeight[i] = definition.MinHeightCm;
                shapeMaxHeight[i] = definition.MaxHeightCm;
                planeOriginX[i] = definition.PlaneOriginXCm;
                planeOriginZ[i] = definition.PlaneOriginZCm;
                planeHeight[i] = definition.PlaneHeightCm;
                planeSlopeX[i] = definition.PlaneSlopeX;
                planeSlopeZ[i] = definition.PlaneSlopeZ;
                ComputeNormal(definition.PlaneSlopeX, definition.PlaneSlopeZ, out normalX[i], out normalY[i], out normalZ[i]);
                StructurePointCm[] vertices = definition.Vertices ?? Array.Empty<StructurePointCm>();
                vertexStart[i] = vertexX.Count;
                vertexCount[i] = vertices.Length;
                for (int v = 0; v < vertexCount[i]; v++)
                {
                    vertexX.Add(vertices[v].Xcm);
                    vertexZ.Add(vertices[v].Zcm);
                }

                centerX[i] = definition.CenterXCm;
                centerZ[i] = definition.CenterZCm;
                halfWidth[i] = definition.HalfWidthCm;
                halfDepth[i] = definition.HalfDepthCm;
                yaw[i] = definition.YawRadians;
                radius[i] = definition.RadiusCm;
                segmentAX[i] = definition.SegmentAXCm;
                segmentAZ[i] = definition.SegmentAZCm;
                segmentBX[i] = definition.SegmentBXCm;
                segmentBZ[i] = definition.SegmentBZCm;
                segmentHalfWidth[i] = definition.SegmentHalfWidthCm;
            }

            var shapes = new StructureShapeSoA(
                shapeKinds,
                shapeBounds,
                shapeMinHeight,
                shapeMaxHeight,
                planeOriginX,
                planeOriginZ,
                planeHeight,
                planeSlopeX,
                planeSlopeZ,
                normalX,
                normalY,
                normalZ,
                vertexStart,
                vertexCount,
                vertexX.ToArray(),
                vertexZ.ToArray(),
                centerX,
                centerZ,
                halfWidth,
                halfDepth,
                yaw,
                radius,
                segmentAX,
                segmentAZ,
                segmentBX,
                segmentBZ,
                segmentHalfWidth);

            StructureSurfaceSoA surfaces = BuildSurfaces(surfaceDefinitions, shapeIndexById, shapes);
            BuildChunkIndex(
                header,
                surfaces,
                out StructureChunkIndexEntry[] chunks,
                out int[] chunkSurfaceIndices,
                out int[] chunkBlockerIndices,
                out int[] chunkPortalIndices,
                out int[] surfaceChunkStart,
                out int[] surfaceChunkCount,
                out int[] surfaceChunkIndices,
                out int chunkColumns,
                out int chunkRows);

            return new StructureCollisionAsset(
                header,
                ToArray(layers),
                ToArray(agentMasks),
                surfaces,
                shapes,
                chunks,
                chunkSurfaceIndices,
                chunkBlockerIndices,
                chunkPortalIndices,
                surfaceChunkStart,
                surfaceChunkCount,
                surfaceChunkIndices,
                chunkColumns,
                chunkRows);
        }

        public static StructureCollisionAsset CreateGridBenchmarkAsset(int columns, int rows, int cellSizeCm)
        {
            if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
            if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));

            var header = new StructureCollisionHeader(
                version: 1,
                new WorldAabbCm(0, 0, checked(columns * cellSizeCm), checked(rows * cellSizeCm)),
                cellSizeCm,
                revision: 1,
                coordinateScale: 1f);
            var layers = new[] { new StructureLayerDefinition("benchmark", 0) };
            var masks = new[] { new StructureAgentMaskDefinition("all", uint.MaxValue) };
            int count = checked(columns * rows);
            var shapes = new StructureShapeDefinition[count];
            var surfaces = new StructureSurfaceDefinition[count];
            for (int i = 0; i < count; i++)
            {
                int col = i % columns;
                int row = i / columns;
                int x = col * cellSizeCm;
                int z = row * cellSizeCm;
                string id = "bench_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                shapes[i] = new StructureShapeDefinition
                {
                    Id = id,
                    Kind = StructureShapeKind.WalkablePolygon,
                    Vertices = new[]
                    {
                        new StructurePointCm(x, z),
                        new StructurePointCm(x + cellSizeCm, z),
                        new StructurePointCm(x + cellSizeCm, z + cellSizeCm),
                        new StructurePointCm(x, z + cellSizeCm)
                    },
                    PlaneOriginXCm = x,
                    PlaneOriginZCm = z,
                    PlaneHeightCm = 100f,
                    MinHeightCm = 100f,
                    MaxHeightCm = 100f
                };
                surfaces[i] = new StructureSurfaceDefinition
                {
                    SurfaceId = i + 1,
                    Kind = StructureSurfaceKind.Platform,
                    Flags = StructureSurfaceFlags.Walkable,
                    LayerId = 0,
                    AgentMask = uint.MaxValue,
                    ShapeId = id
                };
            }

            return Build(header, layers, masks, shapes, surfaces);
        }

        private static StructureSurfaceSoA BuildSurfaces(
            IReadOnlyList<StructureSurfaceDefinition> surfaceDefinitions,
            Dictionary<string, int> shapeIndexById,
            StructureShapeSoA shapes)
        {
            if (surfaceDefinitions.Count == 0)
            {
                throw new InvalidOperationException("Structure collision asset requires at least one surface.");
            }

            int count = surfaceDefinitions.Count;
            var surfaceIds = new int[count];
            var kinds = new StructureSurfaceKind[count];
            var flags = new StructureSurfaceFlags[count];
            var layerIds = new int[count];
            var agentMasks = new uint[count];
            var bounds = new WorldAabbCm[count];
            var minHeight = new float[count];
            var maxHeight = new float[count];
            var normalX = new float[count];
            var normalY = new float[count];
            var normalZ = new float[count];
            var slopeDegrees = new float[count];
            var shapeRefs = new int[count];
            var sourcePrefabIds = new int[count];
            var sourcePartIds = new int[count];
            var usedIds = new HashSet<int>();

            for (int i = 0; i < count; i++)
            {
                StructureSurfaceDefinition definition = surfaceDefinitions[i] ?? throw new InvalidOperationException($"Structure surface at index {i} is null.");
                if (definition.SurfaceId <= 0)
                {
                    throw new InvalidOperationException($"Structure surface at index {i} must have a positive id.");
                }

                if (!usedIds.Add(definition.SurfaceId))
                {
                    throw new InvalidOperationException($"Duplicate structure surface id '{definition.SurfaceId}'.");
                }

                if (definition.AgentMask == 0)
                {
                    throw new InvalidOperationException($"Structure surface '{definition.SurfaceId}' has empty agent mask.");
                }

                if (!shapeIndexById.TryGetValue(definition.ShapeId, out int shapeIndex))
                {
                    throw new InvalidOperationException($"Structure surface '{definition.SurfaceId}' references unknown shape '{definition.ShapeId}'.");
                }

                surfaceIds[i] = definition.SurfaceId;
                kinds[i] = definition.Kind;
                flags[i] = definition.Flags;
                layerIds[i] = definition.LayerId;
                agentMasks[i] = definition.AgentMask;
                shapeRefs[i] = shapeIndex;
                bounds[i] = definition.Bounds ?? shapes.Bounds[shapeIndex];
                minHeight[i] = definition.MinHeightCm ?? shapes.MinHeightCm[shapeIndex];
                maxHeight[i] = definition.MaxHeightCm ?? shapes.MaxHeightCm[shapeIndex];
                normalX[i] = shapes.NormalX[shapeIndex];
                normalY[i] = shapes.NormalY[shapeIndex];
                normalZ[i] = shapes.NormalZ[shapeIndex];
                slopeDegrees[i] = ComputeSlopeDegrees(normalY[i]);
                sourcePrefabIds[i] = definition.SourcePrefabId;
                sourcePartIds[i] = definition.SourcePartId;
            }

            return new StructureSurfaceSoA(
                surfaceIds,
                kinds,
                flags,
                layerIds,
                agentMasks,
                bounds,
                minHeight,
                maxHeight,
                normalX,
                normalY,
                normalZ,
                slopeDegrees,
                shapeRefs,
                sourcePrefabIds,
                sourcePartIds);
        }

        private static void BuildChunkIndex(
            StructureCollisionHeader header,
            StructureSurfaceSoA surfaces,
            out StructureChunkIndexEntry[] chunks,
            out int[] chunkSurfaceIndices,
            out int[] chunkBlockerIndices,
            out int[] chunkPortalIndices,
            out int[] surfaceChunkStart,
            out int[] surfaceChunkCount,
            out int[] surfaceChunkIndices,
            out int chunkColumns,
            out int chunkRows)
        {
            WorldAabbCm world = header.WorldBounds;
            chunkColumns = Math.Max(1, (world.Width + header.ChunkSizeCm - 1) / header.ChunkSizeCm);
            chunkRows = Math.Max(1, (world.Height + header.ChunkSizeCm - 1) / header.ChunkSizeCm);
            int chunkCount = checked(chunkColumns * chunkRows);
            var chunkLists = new ChunkBuildLists[chunkCount];
            for (int i = 0; i < chunkLists.Length; i++)
            {
                chunkLists[i] = new ChunkBuildLists();
            }

            var perSurfaceChunks = new List<int>(surfaces.Count);
            surfaceChunkStart = new int[surfaces.Count];
            surfaceChunkCount = new int[surfaces.Count];

            for (int surfaceIndex = 0; surfaceIndex < surfaces.Count; surfaceIndex++)
            {
                WorldAabbCm bounds = surfaces.Bounds[surfaceIndex];
                ResolveChunkRange(header, chunkColumns, chunkRows, in bounds, out int minChunkX, out int maxChunkX, out int minChunkZ, out int maxChunkZ);
                surfaceChunkStart[surfaceIndex] = perSurfaceChunks.Count;
                for (int cz = minChunkZ; cz <= maxChunkZ; cz++)
                {
                    for (int cx = minChunkX; cx <= maxChunkX; cx++)
                    {
                        int chunkIndex = cz * chunkColumns + cx;
                        ChunkBuildLists lists = chunkLists[chunkIndex];
                        (lists.Surfaces ??= new List<int>()).Add(surfaceIndex);
                        if ((surfaces.Flags[surfaceIndex] & (StructureSurfaceFlags.BlocksMovement | StructureSurfaceFlags.BlocksProjectiles | StructureSurfaceFlags.BlocksVision)) != 0)
                        {
                            (lists.Blockers ??= new List<int>()).Add(surfaceIndex);
                        }

                        if (surfaces.Kinds[surfaceIndex] == StructureSurfaceKind.Portal)
                        {
                            (lists.Portals ??= new List<int>()).Add(surfaceIndex);
                        }

                        perSurfaceChunks.Add(chunkIndex);
                    }
                }

                surfaceChunkCount[surfaceIndex] = perSurfaceChunks.Count - surfaceChunkStart[surfaceIndex];
            }

            chunks = new StructureChunkIndexEntry[chunkCount];
            var flatSurfaces = new List<int>();
            var flatBlockers = new List<int>();
            var flatPortals = new List<int>();
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                ChunkBuildLists lists = chunkLists[chunkIndex];
                int surfaceStart = flatSurfaces.Count;
                AddAll(flatSurfaces, lists.Surfaces);
                int blockerStart = flatBlockers.Count;
                AddAll(flatBlockers, lists.Blockers);
                int portalStart = flatPortals.Count;
                AddAll(flatPortals, lists.Portals);
                chunks[chunkIndex] = new StructureChunkIndexEntry(
                    surfaceStart,
                    flatSurfaces.Count - surfaceStart,
                    blockerStart,
                    flatBlockers.Count - blockerStart,
                    portalStart,
                    flatPortals.Count - portalStart);
            }

            chunkSurfaceIndices = flatSurfaces.ToArray();
            chunkBlockerIndices = flatBlockers.ToArray();
            chunkPortalIndices = flatPortals.ToArray();
            surfaceChunkIndices = perSurfaceChunks.ToArray();
        }

        private static void ResolveChunkRange(
            StructureCollisionHeader header,
            int chunkColumns,
            int chunkRows,
            in WorldAabbCm bounds,
            out int minChunkX,
            out int maxChunkX,
            out int minChunkZ,
            out int maxChunkZ)
        {
            WorldAabbCm world = header.WorldBounds;
            int rightExclusive = Math.Max(bounds.Left, bounds.Right - 1);
            int bottomExclusive = Math.Max(bounds.Top, bounds.Bottom - 1);
            minChunkX = Math.Clamp((bounds.Left - world.Left) / header.ChunkSizeCm, 0, chunkColumns - 1);
            maxChunkX = Math.Clamp((rightExclusive - world.Left) / header.ChunkSizeCm, 0, chunkColumns - 1);
            minChunkZ = Math.Clamp((bounds.Top - world.Top) / header.ChunkSizeCm, 0, chunkRows - 1);
            maxChunkZ = Math.Clamp((bottomExclusive - world.Top) / header.ChunkSizeCm, 0, chunkRows - 1);
        }

        private static void AddAll(List<int> target, List<int>? source)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                target.Add(source[i]);
            }
        }

        private static void ValidateShape(StructureShapeDefinition definition)
        {
            if (!float.IsFinite(definition.MinHeightCm) ||
                !float.IsFinite(definition.MaxHeightCm) ||
                definition.MaxHeightCm < definition.MinHeightCm)
            {
                throw new InvalidOperationException($"Structure shape '{definition.Id}' has invalid height band.");
            }

            if (!float.IsFinite(definition.PlaneHeightCm) ||
                !float.IsFinite(definition.PlaneSlopeX) ||
                !float.IsFinite(definition.PlaneSlopeZ))
            {
                throw new InvalidOperationException($"Structure shape '{definition.Id}' has invalid plane values.");
            }

            int vertexCount = definition.Vertices?.Length ?? 0;
            if ((definition.Kind == StructureShapeKind.WalkablePolygon ||
                 definition.Kind == StructureShapeKind.RampPlane ||
                 definition.Kind == StructureShapeKind.ConvexPrism ||
                 definition.Kind == StructureShapeKind.PortalLink) &&
                (vertexCount < 3 || vertexCount > 16))
            {
                throw new InvalidOperationException($"Structure shape '{definition.Id}' requires 3..16 polygon vertices.");
            }

            if (definition.Kind == StructureShapeKind.Cylinder && definition.RadiusCm <= 0f)
            {
                throw new InvalidOperationException($"Structure cylinder shape '{definition.Id}' requires positive radius.");
            }

            if (definition.Kind == StructureShapeKind.OrientedBox &&
                (definition.HalfWidthCm <= 0f || definition.HalfDepthCm <= 0f))
            {
                throw new InvalidOperationException($"Structure oriented box shape '{definition.Id}' requires positive half extents.");
            }

            if (definition.Kind == StructureShapeKind.WallSegment && definition.SegmentHalfWidthCm <= 0f)
            {
                throw new InvalidOperationException($"Structure wall segment shape '{definition.Id}' requires positive segmentHalfWidthCm.");
            }
        }

        private static WorldAabbCm ResolveShapeBounds(StructureShapeDefinition definition)
        {
            if (definition.Bounds.Width > 0 && definition.Bounds.Height > 0)
            {
                return definition.Bounds;
            }

            switch (definition.Kind)
            {
                case StructureShapeKind.Cylinder:
                    return FloatBounds(
                        definition.CenterXCm - definition.RadiusCm,
                        definition.CenterZCm - definition.RadiusCm,
                        definition.CenterXCm + definition.RadiusCm,
                        definition.CenterZCm + definition.RadiusCm);
                case StructureShapeKind.OrientedBox:
                    return OrientedBoxBounds(definition);
                case StructureShapeKind.WallSegment:
                    return FloatBounds(
                        MathF.Min(definition.SegmentAXCm, definition.SegmentBXCm) - definition.SegmentHalfWidthCm,
                        MathF.Min(definition.SegmentAZCm, definition.SegmentBZCm) - definition.SegmentHalfWidthCm,
                        MathF.Max(definition.SegmentAXCm, definition.SegmentBXCm) + definition.SegmentHalfWidthCm,
                        MathF.Max(definition.SegmentAZCm, definition.SegmentBZCm) + definition.SegmentHalfWidthCm);
                default:
                    return PolygonBounds(definition);
            }
        }

        private static WorldAabbCm PolygonBounds(StructureShapeDefinition definition)
        {
            float minX = float.PositiveInfinity;
            float minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;
            for (int i = 0; i < definition.Vertices.Length; i++)
            {
                minX = MathF.Min(minX, definition.Vertices[i].Xcm);
                minZ = MathF.Min(minZ, definition.Vertices[i].Zcm);
                maxX = MathF.Max(maxX, definition.Vertices[i].Xcm);
                maxZ = MathF.Max(maxZ, definition.Vertices[i].Zcm);
            }

            return FloatBounds(minX, minZ, maxX, maxZ);
        }

        private static WorldAabbCm OrientedBoxBounds(StructureShapeDefinition definition)
        {
            float c = MathF.Cos(definition.YawRadians);
            float s = MathF.Sin(definition.YawRadians);
            float hx = definition.HalfWidthCm;
            float hz = definition.HalfDepthCm;
            float ex = MathF.Abs(c * hx) + MathF.Abs(s * hz);
            float ez = MathF.Abs(s * hx) + MathF.Abs(c * hz);
            return FloatBounds(
                definition.CenterXCm - ex,
                definition.CenterZCm - ez,
                definition.CenterXCm + ex,
                definition.CenterZCm + ez);
        }

        private static WorldAabbCm FloatBounds(float minX, float minZ, float maxX, float maxZ)
        {
            int x = (int)MathF.Floor(minX);
            int z = (int)MathF.Floor(minZ);
            int right = (int)MathF.Ceiling(maxX);
            int bottom = (int)MathF.Ceiling(maxZ);
            return new WorldAabbCm(x, z, Math.Max(1, right - x), Math.Max(1, bottom - z));
        }

        private static void ComputeNormal(float slopeX, float slopeZ, out float normalX, out float normalY, out float normalZ)
        {
            float x = -slopeX;
            float y = 1f;
            float z = -slopeZ;
            float len = MathF.Sqrt((x * x) + (y * y) + (z * z));
            if (len <= 0f || !float.IsFinite(len))
            {
                normalX = 0f;
                normalY = 1f;
                normalZ = 0f;
                return;
            }

            normalX = x / len;
            normalY = y / len;
            normalZ = z / len;
        }

        private static float ComputeSlopeDegrees(float normalY)
        {
            float clamped = Math.Clamp(normalY, -1f, 1f);
            return MathF.Acos(clamped) * (180f / MathF.PI);
        }

        private static T[] ToArray<T>(IReadOnlyList<T> values)
        {
            var result = new T[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                result[i] = values[i];
            }

            return result;
        }
    }
}
