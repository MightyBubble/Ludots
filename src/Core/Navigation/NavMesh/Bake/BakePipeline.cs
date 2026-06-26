using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Map.Fields;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Result of the CDT baking pipeline.
    /// </summary>
    public readonly struct BakePipelineResult
    {
        public readonly bool Success;
        public readonly NavTile Tile;
        public readonly NavBakeArtifact Artifact;

        public BakePipelineResult(bool success, NavTile tile, NavBakeArtifact artifact)
        {
            Success = success;
            Tile = tile;
            Artifact = artifact;
        }
    }

    /// <summary>
    /// Intermediate data for debugging and artifact generation.
    /// </summary>
    public sealed class BakePipelineContext
    {
        public TriWalkMask WalkMask;
        public List<IntRing> ContourRings;
        public ValidPolygonSet PolygonSet;
        public TriMesh TriMesh;
        public NavBakeStage CurrentStage;
        public readonly List<string> Logs = new List<string>();

        public void Log(string message)
        {
            Logs.Add($"[{CurrentStage}] {message}");
        }
    }

    /// <summary>
    /// CDT NavMesh baking pipeline that orchestrates the entire bake process.
    /// </summary>
    public static class BakePipeline
    {
        /// <summary>
        /// Executes the full CDT baking pipeline.
        /// </summary>
        /// <param name="map">Source vertex map.</param>
        /// <param name="chunkX">Tile X coordinate.</param>
        /// <param name="chunkY">Tile Y coordinate.</param>
        /// <param name="tileVersion">Version number for the tile.</param>
        /// <param name="config">Build configuration.</param>
        /// <param name="context">Optional context for debugging.</param>
        /// <returns>Pipeline result with tile and artifact.</returns>
        public static BakePipelineResult Execute(
            VertexMap map,
            int chunkX,
            int chunkY,
            uint tileVersion,
            in NavBuildConfig config,
            NavObstacleSet obstacles,
            string layerId,
            BakePipelineContext context = null)
        {
            var tileId = new NavTileId(chunkX, chunkY, 0);

            // Validate input
            if (map == null)
            {
                var artifact = CreateErrorArtifact(tileId, tileVersion, NavBakeStage.None, NavBakeErrorCode.InvalidInput, "VertexMap is null.");
                return new BakePipelineResult(false, null, artifact);
            }

            return Execute(new VertexMapLogicTerrainField(map), chunkX, chunkY, tileVersion, config, obstacles, layerId, context);
        }

        public static BakePipelineResult Execute(
            LogicTerrainField terrain,
            int chunkX,
            int chunkY,
            uint tileVersion,
            in NavBuildConfig config,
            NavObstacleSet obstacles,
            string layerId,
            BakePipelineContext context = null)
        {
            context ??= new BakePipelineContext();
            var tileId = new NavTileId(chunkX, chunkY, 0);

            if (obstacles == null)
            {
                throw new InvalidOperationException("BakePipeline requires an explicit NavObstacleSet.");
            }

            if (string.IsNullOrWhiteSpace(layerId) ||
                !string.Equals(layerId.Trim(), layerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("BakePipeline requires an explicit non-empty trimmed nav layer id.");
            }

            if (terrain == null)
            {
                var artifact = CreateErrorArtifact(tileId, tileVersion, NavBakeStage.None, NavBakeErrorCode.InvalidInput, "LogicTerrainField is null.");
                return new BakePipelineResult(false, null, artifact);
            }

            int tileWidth = terrain.TileWidthCells(chunkX);
            int tileHeight = terrain.TileHeightCells(chunkY);
            int startC = chunkX * terrain.ChunkSizeCells;
            int startR = chunkY * terrain.ChunkSizeCells;
            int mapWidth = terrain.WidthCells;
            int mapHeight = terrain.HeightCells;

            if (startC < 0 || startR < 0 || startC >= mapWidth || startR >= mapHeight)
            {
                var artifact = CreateErrorArtifact(tileId, tileVersion, NavBakeStage.None, NavBakeErrorCode.InvalidInput, "Tile out of range.");
                return new BakePipelineResult(false, null, artifact);
            }

            // Stage 1: Build walk mask
            context.CurrentStage = NavBakeStage.WalkMask;
            context.Log($"Building walk mask for chunk ({chunkX},{chunkY})...");
            context.WalkMask = WalkMaskBuilder.Build(terrain, chunkX, chunkY, config);
            int obstacleBlockedCount = NavObstacleGeometry.ApplyToWalkMask(
                context.WalkMask,
                terrain,
                chunkX,
                chunkY,
                obstacles,
                layerId);
            if (obstacleBlockedCount > 0)
            {
                context.Log($"Filtered {obstacleBlockedCount} walkable triangles through nav obstacle SSOT.");
            }

            int walkableCount = context.WalkMask.WalkableTriangleCount;
            context.Log($"Found {walkableCount} walkable triangles.");

            if (walkableCount == 0)
            {
                var artifact = CreateErrorArtifact(tileId, tileVersion, NavBakeStage.WalkMask, NavBakeErrorCode.NoWalkableDomain, "No walkable triangles in tile.", context);
                return new BakePipelineResult(false, null, artifact);
            }

            bool cdtSucceeded = TryCdtPipeline(context, startC, startR, config);

            if (!cdtSucceeded)
            {
                var artifact = CreateErrorArtifact(
                    tileId,
                    tileVersion,
                    NavBakeStage.Triangulate,
                    NavBakeErrorCode.TriangulateFailed,
                    "CDT pipeline failed.",
                    context);
                return new BakePipelineResult(false, null, artifact);
            }

            // Stage 5: Convert to NavTile format
            context.CurrentStage = NavBakeStage.Adjacency;
            context.Log("Building adjacency and converting to NavTile format...");

            terrain.GetWorldPositionMeters(startC, startR, out float originXm, out float originZm);
            int originXcm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(originXm));
            int originZcm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(originZm));

            // Convert TriMesh to NavTile format with height sampling
            var result = ConvertTriMeshToNavTile(
                terrain, mapWidth, mapHeight,
                context.TriMesh,
                tileId, tileVersion, config,
                startC, startR, tileWidth, tileHeight, originXm, originZm, originXcm, originZcm,
                context);

            return result;
        }

        /// <summary>
        /// Attempts the full CDT pipeline: Contour → Polygon → CDT.
        /// Returns true if successful (context.TriMesh is set).
        /// Returns true only when CDT produced a valid mesh.
        /// </summary>
        private static bool TryCdtPipeline(BakePipelineContext context, int startC, int startR, in NavBuildConfig config)
        {
            try
            {
                // Stage 2: Extract contours
                context.CurrentStage = NavBakeStage.Contour;
                context.Log("Extracting contour rings...");
                context.ContourRings = ContourExtractor.Extract(context.WalkMask, startC, startR);
                context.Log($"Extracted {context.ContourRings.Count} rings.");

                if (context.ContourRings.Count == 0)
                {
                    context.Log("CDT: No contour rings extracted.");
                    return false;
                }

                // Stage 3: Process polygons
                context.CurrentStage = NavBakeStage.Polygon;
                context.Log("Processing polygons (cleaning, hole assignment)...");
                context.PolygonSet = PolygonProcessor.Process(context.ContourRings, config);
                context.Log($"Processed {context.PolygonSet.Polygons.Length} polygons.");

                if (context.PolygonSet.HasWarnings)
                {
                    foreach (var warning in context.PolygonSet.Warnings)
                        context.Log($"Warning: {warning}");
                }

                if (context.PolygonSet.Polygons.Length == 0)
                {
                    context.Log("CDT: No valid polygons after processing.");
                    return false;
                }

                // Stage 4: CDT triangulation
                context.CurrentStage = NavBakeStage.Triangulate;
                context.Log("Triangulating with CDT...");
                var triangulator = TriangulatorFactory.CreateDefault();

                if (!triangulator.TryTriangulate(context.PolygonSet, out context.TriMesh, out string triError))
                {
                    context.Log($"CDT triangulation failed: {triError}");
                    return false;
                }

                if (context.TriMesh.TriangleCount == 0)
                {
                    context.Log("CDT produced no triangles.");
                    return false;
                }

                context.Log($"CDT: {context.TriMesh.TriangleCount} triangles, {context.TriMesh.VertexCount} vertices.");
                return true;
            }
            catch (Exception ex)
            {
                context.Log($"CDT pipeline exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Converts the 2D TriMesh to a full 3D NavTile with height, adjacency, and portals.
        /// </summary>
        private static BakePipelineResult ConvertTriMeshToNavTile(
            LogicTerrainField terrain,
            int mapWidth,
            int mapHeight,
            TriMesh triMesh,
            NavTileId tileId,
            uint tileVersion,
            in NavBuildConfig config,
            int startC,
            int startR,
            int tileWidth,
            int tileHeight,
            float originXm,
            float originZm,
            int originXcm,
            int originZcm,
            BakePipelineContext context)
        {
            // Convert 2D vertices to 3D with height sampling
            var vx = new int[triMesh.VertexCount];
            var vy = new int[triMesh.VertexCount];
            var vz = new int[triMesh.VertexCount];

            // DEBUG: Log first few vertices
            context.Log($"Converting {triMesh.VertexCount} vertices. startC={startC}, startR={startR}, originXm={originXm:F2}, originZm={originZm:F2}");

            for (int i = 0; i < triMesh.VertexCount; i++)
            {
                var v2d = triMesh.Vertices[i];

                // v2d is local tile coordinate (0-63), convert to global grid coordinate
                int globalC = startC + (int)MathF.Floor(v2d.X);
                int globalR = startR + (int)MathF.Floor(v2d.Y);

                terrain.GetWorldPositionMeters(globalC, globalR, out float absoluteX, out float absoluteZ);
                float worldX = absoluteX - originXm;
                float worldZ = absoluteZ - originZm;

                // Sample height from terrain
                float height = SampleHeight(terrain, mapWidth, mapHeight, globalC, globalR, config.HeightScaleMeters);

                vx[i] = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(worldX));
                vy[i] = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(height));
                vz[i] = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(worldZ));

                // DEBUG: Log first 10 vertices
                if (i < 10)
                {
                    context.Log($"  V[{i}]: v2d=({v2d.X:F1},{v2d.Y:F1}) -> global=({globalC},{globalR}) -> world=({worldX:F2},{worldZ:F2}) h={height:F2} -> cm=({vx[i]},{vy[i]},{vz[i]})");
                }
            }

            // Build triangle arrays
            int triCount = triMesh.TriangleCount;
            var triA = new int[triCount];
            var triB = new int[triCount];
            var triC = new int[triCount];
            var triAreaIds = new byte[triCount];

            for (int i = 0; i < triCount; i++)
            {
                triA[i] = triMesh.Triangles[i * 3 + 0];
                triB[i] = triMesh.Triangles[i * 3 + 1];
                triC[i] = triMesh.Triangles[i * 3 + 2];
                triAreaIds[i] = SampleAreaId(terrain, mapWidth, mapHeight, startC, startR, triMesh, i);
            }

            // Build adjacency
            var n0 = new int[triCount];
            var n1 = new int[triCount];
            var n2 = new int[triCount];
            Array.Fill(n0, -1);
            Array.Fill(n1, -1);
            Array.Fill(n2, -1);
            BuildAdjacency(triA, triB, triC, n0, n1, n2);

            context.Log($"Built adjacency for {triCount} triangles.");

            // Build clearance field
            context.CurrentStage = NavBakeStage.Clearance;
            context.Log("Computing clearance field...");
            var cellWalkable = new bool[tileWidth * tileHeight];
            for (int r = 0; r < tileHeight; r++)
            {
                for (int c = 0; c < tileWidth; c++)
                {
                    cellWalkable[r * tileWidth + c] =
                        context.WalkMask.IsWalkable(c, r, 0) || context.WalkMask.IsWalkable(c, r, 1);
                }
            }
            var clearanceCm = ComputeClearanceCmField(cellWalkable, tileWidth, tileHeight, terrain);

            // Build portals
            context.CurrentStage = NavBakeStage.Portal;
            context.Log("Building border portals...");
            var portals = BuildPortals(terrain, mapWidth, mapHeight, startC, startR, tileWidth, tileHeight, originXm, originZm, clearanceCm, config);
            context.Log($"Built {portals.Length} portals.");

            // Create tile
            context.CurrentStage = NavBakeStage.Serialize;
            ulong buildHash = config.ComputeHash();
            var tile = new NavTile(
                tileId,
                tileVersion,
                buildHash,
                0UL,
                originXcm,
                originZcm,
                vx, vy, vz,
                triA, triB, triC,
                n0, n1, n2,
                triAreaIds,
                portals);

            // Serialize and deserialize for checksum
            using (var ms = new System.IO.MemoryStream())
            {
                NavTileBinary.Write(ms, tile);
                ms.Position = 0;
                tile = NavTileBinary.Read(ms);
            }

            context.Log($"Tile serialized. Checksum: {tile.Checksum:X16}");

            var artifact = new NavBakeArtifact(
                tile.TileId,
                tile.TileVersion,
                NavBakeStage.Serialize,
                NavBakeErrorCode.None,
                "",
                context.WalkMask.WalkableTriangleCount,
                tile.VertexCount,
                tile.TriangleCount,
                tile.Portals.Length,
                context.Logs.ToArray());

            return new BakePipelineResult(true, tile, artifact);
        }

        private static float SampleHeight(LogicTerrainField terrain, int mapWidth, int mapHeight, int c, int r, float heightScale)
        {
            if ((uint)c >= (uint)mapWidth || (uint)r >= (uint)mapHeight)
                return 0f;

            byte h = terrain.GetCell(c, r).HeightLevel;
            return h * heightScale;
        }

        private static byte SampleAreaId(
            LogicTerrainField terrain,
            int mapWidth,
            int mapHeight,
            int startC,
            int startR,
            TriMesh triMesh,
            int triangleIndex)
        {
            Vector2 a = triMesh.Vertices[triMesh.Triangles[triangleIndex * 3 + 0]];
            Vector2 b = triMesh.Vertices[triMesh.Triangles[triangleIndex * 3 + 1]];
            Vector2 c = triMesh.Vertices[triMesh.Triangles[triangleIndex * 3 + 2]];
            int globalC = startC + (int)MathF.Floor((a.X + b.X + c.X) / 3f);
            int globalR = startR + (int)MathF.Floor((a.Y + b.Y + c.Y) / 3f);
            if ((uint)globalC >= (uint)mapWidth || (uint)globalR >= (uint)mapHeight)
            {
                return 0;
            }

            return terrain.GetCell(globalC, globalR).AreaId;
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly int A;
            public readonly int B;

            public EdgeKey(int a, int b)
            {
                if (a < b) { A = a; B = b; }
                else { A = b; B = a; }
            }

            public bool Equals(EdgeKey other) => A == other.A && B == other.B;
            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(A, B);
        }

        private readonly struct EdgeRef
        {
            public readonly int TriId;
            public readonly int EdgeId;

            public EdgeRef(int triId, int edgeId)
            {
                TriId = triId;
                EdgeId = edgeId;
            }
        }

        private static void BuildAdjacency(int[] triA, int[] triB, int[] triC, int[] n0, int[] n1, int[] n2)
        {
            var edgeMap = new Dictionary<EdgeKey, EdgeRef>(triA.Length * 2);
            for (int t = 0; t < triA.Length; t++)
            {
                int a = triA[t];
                int b = triB[t];
                int c = triC[t];
                AddEdge(edgeMap, n0, n1, n2, t, 0, a, b);
                AddEdge(edgeMap, n0, n1, n2, t, 1, b, c);
                AddEdge(edgeMap, n0, n1, n2, t, 2, c, a);
            }
        }

        private static void AddEdge(Dictionary<EdgeKey, EdgeRef> map, int[] n0, int[] n1, int[] n2, int triId, int edgeId, int va, int vb)
        {
            var key = new EdgeKey(va, vb);
            if (map.TryGetValue(key, out var other))
            {
                SetNeighbor(n0, n1, n2, triId, edgeId, other.TriId);
                SetNeighbor(n0, n1, n2, other.TriId, other.EdgeId, triId);
            }
            else
            {
                map.Add(key, new EdgeRef(triId, edgeId));
            }
        }

        private static void SetNeighbor(int[] n0, int[] n1, int[] n2, int triId, int edgeId, int neighborTriId)
        {
            if (edgeId == 0) n0[triId] = neighborTriId;
            else if (edgeId == 1) n1[triId] = neighborTriId;
            else n2[triId] = neighborTriId;
        }

        private static int[] ComputeClearanceCmField(bool[] cellWalkable, int tileWidth, int tileHeight, LogicTerrainField terrain)
        {
            int n = tileWidth * tileHeight;
            int[] dist = new int[n];
            var q = new Queue<int>(n);
            int stepCm = Math.Max(1, Math.Min(terrain.HorizontalStepCm, terrain.VerticalStepCm));
            if (stepCm < 1) stepCm = 1;

            for (int i = 0; i < n; i++)
            {
                if (cellWalkable[i])
                {
                    dist[i] = int.MaxValue;
                }
                else
                {
                    dist[i] = 0;
                    q.Enqueue(i);
                }
            }

            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                int baseD = dist[cur];
                int x = cur % tileWidth;
                int y = cur / tileWidth;
                int nd = baseD + stepCm;

                if (x > 0) Relax(cur - 1, nd);
                if (x + 1 < tileWidth) Relax(cur + 1, nd);
                if (y > 0) Relax(cur - tileWidth, nd);
                if (y + 1 < tileHeight) Relax(cur + tileWidth, nd);

                void Relax(int idx, int newDist)
                {
                    if (!cellWalkable[idx]) return;
                    if (newDist >= dist[idx]) return;
                    dist[idx] = newDist;
                    q.Enqueue(idx);
                }
            }

            return dist;
        }

        private static NavBorderPortal[] BuildPortals(
            LogicTerrainField terrain,
            int mapWidth,
            int mapHeight,
            int startC,
            int startR,
            int tileWidth,
            int tileHeight,
            float originXm,
            float originZm,
            int[] clearanceCm,
            in NavBuildConfig config)
        {
            var portals = new List<NavBorderPortal>(SpatialScaleDefaults.NavPortalInitialCapacity);
            int endC = startC + tileWidth;
            int endR = startR + tileHeight;

            AddVerticalPortals(terrain, mapWidth, mapHeight, startC, startR, endR, tileWidth, tileHeight, originXm, originZm, clearanceCm, config, NavPortalSide.West, insideC: startC, outsideC: startC - 1, portals);
            AddVerticalPortals(terrain, mapWidth, mapHeight, endC, startR, endR, tileWidth, tileHeight, originXm, originZm, clearanceCm, config, NavPortalSide.East, insideC: endC - 1, outsideC: endC, portals);
            AddHorizontalPortals(terrain, mapWidth, mapHeight, startR, startC, endC, tileWidth, tileHeight, originXm, originZm, clearanceCm, config, NavPortalSide.North, insideR: startR, outsideR: startR - 1, portals);
            AddHorizontalPortals(terrain, mapWidth, mapHeight, endR, startC, endC, tileWidth, tileHeight, originXm, originZm, clearanceCm, config, NavPortalSide.South, insideR: endR - 1, outsideR: endR, portals);

            return portals.ToArray();
        }

        private static void AddVerticalPortals(
            LogicTerrainField terrain, int mapWidth, int mapHeight,
            int boundaryCol, int startR, int endR,
            int tileWidth, int tileHeight,
            float originXm, float originZm,
            int[] clearanceCm, in NavBuildConfig config,
            NavPortalSide side, int insideC, int outsideC,
            List<NavBorderPortal> dst)
        {
            int segStart = -1;
            for (int r = startR; r < endR; r++)
            {
                bool inside = IsCellAnyTriangleWalkable(terrain, mapWidth, mapHeight, insideC, r, config);
                bool outside = IsCellAnyTriangleWalkable(terrain, mapWidth, mapHeight, outsideC, r, config);
                bool passable = inside && outside;

                int localV = r - startR;
                if (passable)
                {
                    if (segStart < 0) segStart = localV;
                }
                else
                {
                    if (segStart >= 0)
                    {
                        AddPortalSegment(terrain, boundaryCol, startR, segStart, localV, tileWidth, tileHeight, originXm, originZm, clearanceCm, side, true, dst);
                        segStart = -1;
                    }
                }
            }

            if (segStart >= 0)
            {
                AddPortalSegment(terrain, boundaryCol, startR, segStart, endR - startR, tileWidth, tileHeight, originXm, originZm, clearanceCm, side, true, dst);
            }
        }

        private static void AddHorizontalPortals(
            LogicTerrainField terrain, int mapWidth, int mapHeight,
            int boundaryRow, int startC, int endC,
            int tileWidth, int tileHeight,
            float originXm, float originZm,
            int[] clearanceCm, in NavBuildConfig config,
            NavPortalSide side, int insideR, int outsideR,
            List<NavBorderPortal> dst)
        {
            int segStart = -1;
            for (int c = startC; c < endC; c++)
            {
                bool inside = IsCellAnyTriangleWalkable(terrain, mapWidth, mapHeight, c, insideR, config);
                bool outside = IsCellAnyTriangleWalkable(terrain, mapWidth, mapHeight, c, outsideR, config);
                bool passable = inside && outside;

                int localU = c - startC;
                if (passable)
                {
                    if (segStart < 0) segStart = localU;
                }
                else
                {
                    if (segStart >= 0)
                    {
                        AddPortalSegment(terrain, boundaryRow, startC, segStart, localU, tileWidth, tileHeight, originXm, originZm, clearanceCm, side, false, dst);
                        segStart = -1;
                    }
                }
            }

            if (segStart >= 0)
            {
                AddPortalSegment(terrain, boundaryRow, startC, segStart, endC - startC, tileWidth, tileHeight, originXm, originZm, clearanceCm, side, false, dst);
            }
        }

        private static void AddPortalSegment(
            LogicTerrainField terrain,
            int boundary, int start, int seg0, int seg1,
            int tileWidth, int tileHeight,
            float originXm, float originZm,
            int[] clearanceCm, NavPortalSide side, bool isVertical,
            List<NavBorderPortal> dst)
        {
            short u0, v0, u1, v1;
            int x0cm, z0cm, x1cm, z1cm;

            if (isVertical)
            {
                short u = side == NavPortalSide.West ? (short)0 : checked((short)tileWidth);
                u0 = u; u1 = u;
                v0 = (short)seg0;
                v1 = (short)seg1;

                int r0 = start + seg0;
                int r1 = start + seg1;
                terrain.GetWorldPositionMeters(boundary, r0, out float worldX0, out float worldZ0);
                terrain.GetWorldPositionMeters(boundary, r1, out float worldX1, out float worldZ1);
                float x0m = worldX0 - originXm;
                float z0m = worldZ0 - originZm;
                float x1m = worldX1 - originXm;
                float z1m = worldZ1 - originZm;

                x0cm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(x0m));
                z0cm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(z0m));
                x1cm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(x1m));
                z1cm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(z1m));
            }
            else
            {
                short v = side == NavPortalSide.North ? (short)0 : checked((short)tileHeight);
                v0 = v; v1 = v;
                u0 = (short)seg0;
                u1 = (short)seg1;

                int c0 = start + seg0;
                int c1 = start + seg1;
                terrain.GetWorldPositionMeters(c0, boundary, out float worldX0, out float worldZ0);
                terrain.GetWorldPositionMeters(c1, boundary, out float worldX1, out float worldZ1);
                float x0m = worldX0 - originXm;
                float z0m = worldZ0 - originZm;
                float x1m = worldX1 - originXm;
                float z1m = worldZ1 - originZm;

                x0cm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(x0m));
                z0cm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(z0m));
                x1cm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(x1m));
                z1cm = (int)MathF.Round(SpatialScaleDefaults.MetersToCentimeters(z1m));
            }

            int dx = x1cm - x0cm;
            int dz = z1cm - z0cm;
            int len = (int)MathF.Round(MathF.Sqrt(dx * dx + dz * dz));

            int minClearance = int.MaxValue;
            if (isVertical)
            {
                int lc = side == NavPortalSide.West ? 0 : (tileWidth - 1);
                for (int rr = seg0; rr < seg1; rr++)
                {
                    int idx = rr * tileWidth + lc;
                    if (idx < clearanceCm.Length && clearanceCm[idx] < minClearance)
                        minClearance = clearanceCm[idx];
                }
            }
            else
            {
                int lr = side == NavPortalSide.North ? 0 : (tileHeight - 1);
                for (int cc = seg0; cc < seg1; cc++)
                {
                    int idx = lr * tileWidth + cc;
                    if (idx < clearanceCm.Length && clearanceCm[idx] < minClearance)
                        minClearance = clearanceCm[idx];
                }
            }

            int clearance = Math.Max(0, Math.Min(len / 2, minClearance == int.MaxValue ? 0 : minClearance));
            dst.Add(new NavBorderPortal(side, u0, v0, u1, v1, x0cm, z0cm, x1cm, z1cm, clearance));
        }

        private static bool IsCellAnyTriangleWalkable(LogicTerrainField terrain, int mapWidth, int mapHeight, int c, int r, in NavBuildConfig config)
        {
            if (r < 0 || c < 0 || r >= mapHeight - 1 || c >= mapWidth - 1) return false;
            bool isOdd = terrain.Topology == LogicTerrainTopology.Hex && (r & 1) == 1;

            var v1 = GetWalkVertex(terrain, mapWidth, mapHeight, c, r, config.HeightScaleMeters);
            WalkVertex t1p1, t1p2, t1p3;
            WalkVertex t2p1, t2p2, t2p3;

            if (!isOdd)
            {
                t1p1 = v1;
                t1p2 = GetWalkVertex(terrain, mapWidth, mapHeight, c + 1, r, config.HeightScaleMeters);
                t1p3 = GetWalkVertex(terrain, mapWidth, mapHeight, c, r + 1, config.HeightScaleMeters);

                t2p1 = t1p2;
                t2p2 = GetWalkVertex(terrain, mapWidth, mapHeight, c + 1, r + 1, config.HeightScaleMeters);
                t2p3 = t1p3;
            }
            else
            {
                t1p1 = v1;
                t1p2 = GetWalkVertex(terrain, mapWidth, mapHeight, c + 1, r, config.HeightScaleMeters);
                t1p3 = GetWalkVertex(terrain, mapWidth, mapHeight, c + 1, r + 1, config.HeightScaleMeters);

                t2p1 = v1;
                t2p2 = t1p3;
                t2p3 = GetWalkVertex(terrain, mapWidth, mapHeight, c, r + 1, config.HeightScaleMeters);
            }

            return IsTriWalkable(t1p1, t1p2, t1p3, config) || IsTriWalkable(t2p1, t2p2, t2p3, config);
        }

        private static WalkVertex GetWalkVertex(LogicTerrainField terrain, int mapWidth, int mapHeight, int c, int r, float heightScale)
        {
            byte h = 0;
            byte w = 0;
            bool ramp = false;
            bool blocked = false;

            if ((uint)c < (uint)mapWidth && (uint)r < (uint)mapHeight)
            {
                LogicTerrainCell cell = terrain.GetCell(c, r);
                h = cell.HeightLevel;
                w = cell.WaterHeightLevel;
                ramp = cell.IsRamp;
                blocked = cell.IsBlocked;
            }

            return new WalkVertex(c, r, h, w, ramp, blocked);
        }

        private static bool IsTriWalkable(in WalkVertex a, in WalkVertex b, in WalkVertex c, in NavBuildConfig config)
        {
            if (a.IsBlocked || b.IsBlocked || c.IsBlocked) return false;
            if (a.WaterHeight > a.Height || b.WaterHeight > b.Height || c.WaterHeight > c.Height) return false;
            if (a.IsRamp || b.IsRamp || c.IsRamp) return true;
            byte min = Math.Min(a.Height, Math.Min(b.Height, c.Height));
            byte max = Math.Max(a.Height, Math.Max(b.Height, c.Height));
            return (max - min) <= config.CliffHeightThreshold;
        }

        private static NavBakeArtifact CreateErrorArtifact(NavTileId tileId, uint version, NavBakeStage stage, NavBakeErrorCode error, string message, BakePipelineContext context = null)
        {
            return new NavBakeArtifact(tileId, version, stage, error, message, 0, 0, 0, 0, context?.Logs.ToArray());
        }
    }
}
