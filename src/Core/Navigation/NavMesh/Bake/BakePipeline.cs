using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Map.Hex;

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
            BakePipelineContext context = null)
        {
            context ??= new BakePipelineContext();
            var tileId = new NavTileId(chunkX, chunkY, 0);

            // Validate input
            if (map == null)
            {
                var artifact = CreateErrorArtifact(tileId, tileVersion, NavBakeStage.None, NavBakeErrorCode.InvalidInput, "VertexMap is null.");
                return new BakePipelineResult(false, null, artifact);
            }

            int startC = chunkX * VertexChunk.ChunkSize;
            int startR = chunkY * VertexChunk.ChunkSize;
            int mapWidth = map.WidthInChunks * VertexChunk.ChunkSize;
            int mapHeight = map.HeightInChunks * VertexChunk.ChunkSize;

            if (startC < 0 || startR < 0 || startC >= mapWidth || startR >= mapHeight)
            {
                var artifact = CreateErrorArtifact(tileId, tileVersion, NavBakeStage.None, NavBakeErrorCode.InvalidInput, "Tile out of range.");
                return new BakePipelineResult(false, null, artifact);
            }

            // Stage 1: Build walk mask
            context.CurrentStage = NavBakeStage.WalkMask;
            context.Log($"Building walk mask for chunk ({chunkX},{chunkY})...");
            context.WalkMask = WalkMaskBuilder.Build(map, chunkX, chunkY, config);
            int walkableCount = context.WalkMask.WalkableTriangleCount;
            context.Log($"Found {walkableCount} walkable triangles.");

            if (walkableCount == 0)
            {
                var artifact = CreateErrorArtifact(tileId, tileVersion, NavBakeStage.WalkMask, NavBakeErrorCode.NoWalkableDomain, "No walkable triangles in tile.", context);
                return new BakePipelineResult(false, null, artifact);
            }

            // Stage 2-4: CDT pipeline (primary) with GridMesh fallback.
            //
            // Primary path: Contour → Polygon → CDT
            //   Produces minimal triangle count with Delaunay quality.
            //
            // Fallback path: GridMesh (direct grid triangles)
            //   Guaranteed correct for any walkable mask; more triangles but no
            //   triangulation quality issues. Used when CDT fails on degenerate input.

            bool cdtSucceeded = TryCdtPipeline(context, startC, startR, config);

            if (!cdtSucceeded)
            {
                context.CurrentStage = NavBakeStage.Triangulate;
                context.Log("CDT pipeline failed, falling back to grid mesh...");

                if (!GridMeshBuilder.TryBuild(context.WalkMask, startC, startR,
                    out context.TriMesh, out string meshError))
                {
                    var artifact = CreateErrorArtifact(tileId, tileVersion, NavBakeStage.Triangulate,
                        NavBakeErrorCode.TriangulateFailed,
                        meshError ?? "Both CDT and grid mesh failed.", context);
                    return new BakePipelineResult(false, null, artifact);
                }

                context.Log($"Grid mesh fallback: {context.TriMesh.TriangleCount} triangles, {context.TriMesh.VertexCount} vertices.");
            }

            // Stage 5: Convert to NavTile format
            context.CurrentStage = NavBakeStage.Adjacency;
            context.Log("Building adjacency and converting to NavTile format...");

            float originXm = startC * HexCoordinates.HexWidth;
            float originZm = startR * HexCoordinates.RowSpacing;
            int originXcm = (int)MathF.Round(originXm * 100f);
            int originZcm = (int)MathF.Round(originZm * 100f);

            // Convert TriMesh to NavTile format with height sampling
            var result = ConvertTriMeshToNavTile(
                map, mapWidth, mapHeight,
                context.TriMesh,
                tileId, tileVersion, config,
                startC, startR, originXm, originZm, originXcm, originZcm,
                context);

            return result;
        }

        /// <summary>
        /// Attempts the full CDT pipeline: Contour → Polygon → CDT.
        /// Returns true if successful (context.TriMesh is set).
        /// Returns false on any failure (caller should fall back to GridMesh).
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
            VertexMap map,
            int mapWidth,
            int mapHeight,
            TriMesh triMesh,
            NavTileId tileId,
            uint tileVersion,
            in NavBuildConfig config,
            int startC,
            int startR,
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

                // Convert global grid to world coordinates (local to tile origin)
                // Account for hex grid staggering
                float worldX = HexCoordinates.HexWidth * (globalC + 0.5f * (globalR & 1)) - originXm;
                float worldZ = HexCoordinates.RowSpacing * globalR - originZm;

                // Sample height from terrain
                float height = SampleHeight(map, mapWidth, mapHeight, globalC, globalR, config.HeightScaleMeters);

                vx[i] = (int)MathF.Round(worldX * 100f);
                vy[i] = (int)MathF.Round(height * 100f);
                vz[i] = (int)MathF.Round(worldZ * 100f);

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

            for (int i = 0; i < triCount; i++)
            {
                triA[i] = triMesh.Triangles[i * 3 + 0];
                triB[i] = triMesh.Triangles[i * 3 + 1];
                triC[i] = triMesh.Triangles[i * 3 + 2];
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
            var cellWalkable = new bool[VertexChunk.ChunkSize * VertexChunk.ChunkSize];
            for (int r = 0; r < VertexChunk.ChunkSize; r++)
            {
                for (int c = 0; c < VertexChunk.ChunkSize; c++)
                {
                    cellWalkable[r * VertexChunk.ChunkSize + c] =
                        context.WalkMask.IsWalkable(c, r, 0) || context.WalkMask.IsWalkable(c, r, 1);
                }
            }
            var clearanceCm = ComputeClearanceCmField(cellWalkable);

            // Build portals
            context.CurrentStage = NavBakeStage.Portal;
            context.Log("Building border portals...");
            var portals = BuildPortals(map, mapWidth, mapHeight, startC, startR, originXm, originZm, clearanceCm, config);
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

        private static float SampleHeight(VertexMap map, int mapWidth, int mapHeight, int c, int r, float heightScale)
        {
            if ((uint)c >= (uint)mapWidth || (uint)r >= (uint)mapHeight)
                return 0f;

            var chunk = map.GetChunk(c, r, false);
            if (chunk == null)
                return 0f;

            int lx = c & VertexChunk.ChunkSizeMask;
            int lr = r & VertexChunk.ChunkSizeMask;
            byte h = chunk.GetHeight(lx, lr);
            return h * heightScale;
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

        private static int[] ComputeClearanceCmField(bool[] cellWalkable)
        {
            int n = VertexChunk.ChunkSize * VertexChunk.ChunkSize;
            int[] dist = new int[n];
            var q = new Queue<int>(n);
            int stepCm = (int)MathF.Round(MathF.Min(HexCoordinates.HexWidth, HexCoordinates.RowSpacing) * 100f);
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
                int x = cur % VertexChunk.ChunkSize;
                int y = cur / VertexChunk.ChunkSize;
                int nd = baseD + stepCm;

                if (x > 0) Relax(cur - 1, nd);
                if (x + 1 < VertexChunk.ChunkSize) Relax(cur + 1, nd);
                if (y > 0) Relax(cur - VertexChunk.ChunkSize, nd);
                if (y + 1 < VertexChunk.ChunkSize) Relax(cur + VertexChunk.ChunkSize, nd);

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

        private static NavBorderPortal[] BuildPortals(VertexMap map, int mapWidth, int mapHeight, int startC, int startR, float originXm, float originZm, int[] clearanceCm, in NavBuildConfig config)
        {
            var portals = new List<NavBorderPortal>(64);
            int endC = startC + VertexChunk.ChunkSize;
            int endR = startR + VertexChunk.ChunkSize;

            AddVerticalPortals(map, mapWidth, mapHeight, startC, startR, endR, originXm, originZm, clearanceCm, config, NavPortalSide.West, insideC: startC, outsideC: startC - 1, portals);
            AddVerticalPortals(map, mapWidth, mapHeight, endC, startR, endR, originXm, originZm, clearanceCm, config, NavPortalSide.East, insideC: endC - 1, outsideC: endC, portals);
            AddHorizontalPortals(map, mapWidth, mapHeight, startR, startC, endC, originXm, originZm, clearanceCm, config, NavPortalSide.North, insideR: startR, outsideR: startR - 1, portals);
            AddHorizontalPortals(map, mapWidth, mapHeight, endR, startC, endC, originXm, originZm, clearanceCm, config, NavPortalSide.South, insideR: endR - 1, outsideR: endR, portals);

            return portals.ToArray();
        }

        private static void AddVerticalPortals(
            VertexMap map, int mapWidth, int mapHeight,
            int boundaryCol, int startR, int endR,
            float originXm, float originZm,
            int[] clearanceCm, in NavBuildConfig config,
            NavPortalSide side, int insideC, int outsideC,
            List<NavBorderPortal> dst)
        {
            int segStart = -1;
            for (int r = startR; r < endR; r++)
            {
                bool inside = IsCellAnyTriangleWalkable(map, mapWidth, mapHeight, insideC, r, config);
                bool outside = IsCellAnyTriangleWalkable(map, mapWidth, mapHeight, outsideC, r, config);
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
                        AddPortalSegment(boundaryCol, startR, segStart, localV, originXm, originZm, clearanceCm, side, true, dst);
                        segStart = -1;
                    }
                }
            }

            if (segStart >= 0)
            {
                AddPortalSegment(boundaryCol, startR, segStart, endR - startR, originXm, originZm, clearanceCm, side, true, dst);
            }
        }

        private static void AddHorizontalPortals(
            VertexMap map, int mapWidth, int mapHeight,
            int boundaryRow, int startC, int endC,
            float originXm, float originZm,
            int[] clearanceCm, in NavBuildConfig config,
            NavPortalSide side, int insideR, int outsideR,
            List<NavBorderPortal> dst)
        {
            int segStart = -1;
            for (int c = startC; c < endC; c++)
            {
                bool inside = IsCellAnyTriangleWalkable(map, mapWidth, mapHeight, c, insideR, config);
                bool outside = IsCellAnyTriangleWalkable(map, mapWidth, mapHeight, c, outsideR, config);
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
                        AddPortalSegment(boundaryRow, startC, segStart, localU, originXm, originZm, clearanceCm, side, false, dst);
                        segStart = -1;
                    }
                }
            }

            if (segStart >= 0)
            {
                AddPortalSegment(boundaryRow, startC, segStart, endC - startC, originXm, originZm, clearanceCm, side, false, dst);
            }
        }

        private static void AddPortalSegment(
            int boundary, int start, int seg0, int seg1,
            float originXm, float originZm,
            int[] clearanceCm, NavPortalSide side, bool isVertical,
            List<NavBorderPortal> dst)
        {
            short u0, v0, u1, v1;
            int x0cm, z0cm, x1cm, z1cm;

            if (isVertical)
            {
                short u = side == NavPortalSide.West ? (short)0 : (short)VertexChunk.ChunkSize;
                u0 = u; u1 = u;
                v0 = (short)seg0;
                v1 = (short)seg1;

                int r0 = start + seg0;
                int r1 = start + seg1;
                float x0m = HexCoordinates.HexWidth * (boundary + 0.5f * (r0 & 1)) - originXm;
                float z0m = HexCoordinates.RowSpacing * r0 - originZm;
                float x1m = HexCoordinates.HexWidth * (boundary + 0.5f * (r1 & 1)) - originXm;
                float z1m = HexCoordinates.RowSpacing * r1 - originZm;

                x0cm = (int)MathF.Round(x0m * 100f);
                z0cm = (int)MathF.Round(z0m * 100f);
                x1cm = (int)MathF.Round(x1m * 100f);
                z1cm = (int)MathF.Round(z1m * 100f);
            }
            else
            {
                short v = side == NavPortalSide.North ? (short)0 : (short)VertexChunk.ChunkSize;
                v0 = v; v1 = v;
                u0 = (short)seg0;
                u1 = (short)seg1;

                int c0 = start + seg0;
                int c1 = start + seg1;
                float x0m = HexCoordinates.HexWidth * (c0 + 0.5f * (boundary & 1)) - originXm;
                float z0m = HexCoordinates.RowSpacing * boundary - originZm;
                float x1m = HexCoordinates.HexWidth * (c1 + 0.5f * (boundary & 1)) - originXm;
                float z1m = z0m;

                x0cm = (int)MathF.Round(x0m * 100f);
                z0cm = (int)MathF.Round(z0m * 100f);
                x1cm = (int)MathF.Round(x1m * 100f);
                z1cm = (int)MathF.Round(z1m * 100f);
            }

            int dx = x1cm - x0cm;
            int dz = z1cm - z0cm;
            int len = (int)MathF.Round(MathF.Sqrt(dx * dx + dz * dz));

            int minClearance = int.MaxValue;
            if (isVertical)
            {
                int lc = side == NavPortalSide.West ? 0 : (VertexChunk.ChunkSize - 1);
                for (int rr = seg0; rr < seg1; rr++)
                {
                    int idx = rr * VertexChunk.ChunkSize + lc;
                    if (idx < clearanceCm.Length && clearanceCm[idx] < minClearance)
                        minClearance = clearanceCm[idx];
                }
            }
            else
            {
                int lr = side == NavPortalSide.North ? 0 : (VertexChunk.ChunkSize - 1);
                for (int cc = seg0; cc < seg1; cc++)
                {
                    int idx = lr * VertexChunk.ChunkSize + cc;
                    if (idx < clearanceCm.Length && clearanceCm[idx] < minClearance)
                        minClearance = clearanceCm[idx];
                }
            }

            int clearance = Math.Max(0, Math.Min(len / 2, minClearance == int.MaxValue ? 0 : minClearance));
            dst.Add(new NavBorderPortal(side, u0, v0, u1, v1, x0cm, z0cm, x1cm, z1cm, clearance));
        }

        private static bool IsCellAnyTriangleWalkable(VertexMap map, int mapWidth, int mapHeight, int c, int r, in NavBuildConfig config)
        {
            if (r < 0 || c < 0 || r >= mapHeight - 1 || c >= mapWidth - 1) return false;
            bool isOdd = (r & 1) == 1;

            var v1 = GetWalkVertex(map, mapWidth, mapHeight, c, r, config.HeightScaleMeters);
            WalkVertex t1p1, t1p2, t1p3;
            WalkVertex t2p1, t2p2, t2p3;

            if (!isOdd)
            {
                t1p1 = v1;
                t1p2 = GetWalkVertex(map, mapWidth, mapHeight, c + 1, r, config.HeightScaleMeters);
                t1p3 = GetWalkVertex(map, mapWidth, mapHeight, c, r + 1, config.HeightScaleMeters);

                t2p1 = t1p2;
                t2p2 = GetWalkVertex(map, mapWidth, mapHeight, c + 1, r + 1, config.HeightScaleMeters);
                t2p3 = t1p3;
            }
            else
            {
                t1p1 = v1;
                t1p2 = GetWalkVertex(map, mapWidth, mapHeight, c + 1, r, config.HeightScaleMeters);
                t1p3 = GetWalkVertex(map, mapWidth, mapHeight, c + 1, r + 1, config.HeightScaleMeters);

                t2p1 = v1;
                t2p2 = t1p3;
                t2p3 = GetWalkVertex(map, mapWidth, mapHeight, c, r + 1, config.HeightScaleMeters);
            }

            return IsTriWalkable(t1p1, t1p2, t1p3, config) || IsTriWalkable(t2p1, t2p2, t2p3, config);
        }

        private static WalkVertex GetWalkVertex(VertexMap map, int mapWidth, int mapHeight, int c, int r, float heightScale)
        {
            byte h = 0;
            byte w = 0;
            bool ramp = false;
            bool blocked = false;

            if ((uint)c < (uint)mapWidth && (uint)r < (uint)mapHeight)
            {
                var chunk = map.GetChunk(c, r, false);
                if (chunk != null)
                {
                    int lx = c & VertexChunk.ChunkSizeMask;
                    int lr = r & VertexChunk.ChunkSizeMask;
                    h = chunk.GetHeight(lx, lr);
                    w = chunk.GetWaterHeight(lx, lr);
                    ramp = chunk.GetRamp(lx, lr);
                    blocked = chunk.GetFlag(lx, lr);
                }
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
