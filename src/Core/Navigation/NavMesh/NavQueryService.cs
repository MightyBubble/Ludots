using System;
using System.Collections.Generic;
using Ludots.Core.Collections;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation.NavMesh.Bake;

namespace Ludots.Core.Navigation.NavMesh
{
    public enum NavPathStatus : byte
    {
        Ok = 0,
        NotReady = 1,
        NotReachable = 2,
        InvalidInput = 3
    }

    public readonly struct NavLocation
    {
        public readonly NavTileId TileId;
        public readonly uint TileVersion;
        public readonly int TriangleId;
        public readonly int LocalXcm;
        public readonly int LocalZcm;

        public NavLocation(NavTileId tileId, uint tileVersion, int triangleId, int localXcm, int localZcm)
        {
            TileId = tileId;
            TileVersion = tileVersion;
            TriangleId = triangleId;
            LocalXcm = localXcm;
            LocalZcm = localZcm;
        }
    }

    public readonly struct NavPathResult
    {
        public readonly NavPathStatus Status;
        public readonly int[] PathXcm;
        public readonly int[] PathZcm;
        public readonly Fix64 TravelCost;

        public NavPathResult(NavPathStatus status, int[] pathXcm, int[] pathZcm, Fix64 travelCost)
        {
            Status = status;
            PathXcm = pathXcm ?? Array.Empty<int>();
            PathZcm = pathZcm ?? Array.Empty<int>();
            TravelCost = travelCost;
        }
    }

    public sealed class NavQueryService
    {
        private readonly NavTileStore _store;
        private readonly int _layer;
        private readonly NavAreaCostTable _areaCosts;
        private readonly Fix64 _minCost;
        private readonly int _tileWidthCm;
        private readonly int _tileHeightCm;
        private readonly int _worldMinXcm;
        private readonly int _worldMinZcm;
        private readonly int _bakedTileWidthCm;
        private readonly int _bakedTileHeightCm;
        private readonly object _triangleAdjacencySync = new object();
        private readonly Dictionary<TriangleAdjacencyKey, TriangleAdjacency> _triangleAdjacencyCache = new Dictionary<TriangleAdjacencyKey, TriangleAdjacency>();
        private readonly object _triangleConnectivitySync = new object();
        private readonly Dictionary<TriangleAdjacencyKey, TriangleConnectivity> _triangleConnectivityCache = new Dictionary<TriangleAdjacencyKey, TriangleConnectivity>();

        private const int MaxTriangleAdjacencyCacheEntries = 1024;
        private const int MaxTriangleConnectivityCacheEntries = 1024;
        private const int GeometricEdgeToleranceCm = 4;
        private const int MinSharedEdgeOverlapCm = 8;

        private readonly struct TriangleAdjacencyKey : IEquatable<TriangleAdjacencyKey>
        {
            public readonly NavTileId TileId;
            public readonly uint TileVersion;
            public readonly ulong Checksum;

            public TriangleAdjacencyKey(NavTileId tileId, uint tileVersion, ulong checksum)
            {
                TileId = tileId;
                TileVersion = tileVersion;
                Checksum = checksum;
            }

            public bool Equals(TriangleAdjacencyKey other)
            {
                return TileId.Equals(other.TileId) &&
                    TileVersion == other.TileVersion &&
                    Checksum == other.Checksum;
            }

            public override bool Equals(object obj) => obj is TriangleAdjacencyKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(TileId, TileVersion, Checksum);
        }

        private readonly struct TriangleAdjacency
        {
            public readonly int[][] Neighbors;

            public TriangleAdjacency(int[][] neighbors)
            {
                Neighbors = neighbors ?? Array.Empty<int[]>();
            }
        }

        private readonly struct TriangleConnectivity
        {
            public readonly int[] Components;

            public TriangleConnectivity(int[] components)
            {
                Components = components ?? Array.Empty<int>();
            }
        }

        public NavQueryService(NavTileStore store, int layer = 0, NavAreaCostTable areaCosts = null, int tileWidthCm = 0, int tileHeightCm = 0)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _layer = layer;
            _areaCosts = areaCosts ?? NavAreaCostTable.CreateDefault();
            _minCost = _areaCosts.MinCost;
            _tileWidthCm = tileWidthCm > 0 ? tileWidthCm : (store.TileWidthCm > 0 ? store.TileWidthCm : DefaultTileWidthCm);
            _tileHeightCm = tileHeightCm > 0 ? tileHeightCm : (store.TileHeightCm > 0 ? store.TileHeightCm : DefaultTileHeightCm);
            _worldMinXcm = store.WorldMinXcm;
            _worldMinZcm = store.WorldMinZcm;
            _bakedTileWidthCm = store.BakedTileWidthCm > 0 ? store.BakedTileWidthCm : _tileWidthCm;
            _bakedTileHeightCm = store.BakedTileHeightCm > 0 ? store.BakedTileHeightCm : _tileHeightCm;
        }

        public int DataRevision => _store.Revision;

        public bool TryProject(int worldXcm, int worldZcm, out NavLocation loc)
        {
            loc = default;
            var tileId = LocateTile(worldXcm, worldZcm);
            NavTile tile;
            try
            {
                tile = _store.GetOrLoad(tileId);
            }
            catch
            {
                return false;
            }

            int localXcm = WorldToTileLocalX(tile, worldXcm);
            int localZcm = WorldToTileLocalZ(tile, worldZcm);
            int triId = FindContainingTriangle(tile, localXcm, localZcm);
            if (triId < 0)
            {
                triId = FindNearestTriangle(tile, localXcm, localZcm, ResolvePointProjectionDistanceCm());
            }

            if (triId < 0) return false;
            loc = new NavLocation(tile.TileId, tile.TileVersion, triId, localXcm, localZcm);
            return true;
        }

        public NavPathResult TryFindPath(int startXcm, int startZcm, int goalXcm, int goalZcm, int maxPortals = 256)
        {
            if (!TryProject(startXcm, startZcm, out var startLoc)) return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            if (!TryProject(goalXcm, goalZcm, out var goalLoc)) return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);

            if (startLoc.TileId.Equals(goalLoc.TileId))
            {
                var tile = _store.GetOrLoad(startLoc.TileId);
                if (!TryBuildSameTileTrianglePath(
                        tile,
                        startLoc,
                        goalLoc,
                        startXcm,
                        startZcm,
                        goalXcm,
                        goalZcm,
                        maxPortals,
                        out int[] sameTilePathXcm,
                        out int[] sameTilePathZcm,
                        out Fix64 sameTileCost))
                {
                    return new NavPathResult(NavPathStatus.NotReachable, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
                }

                return new NavPathResult(NavPathStatus.Ok, sameTilePathXcm, sameTilePathZcm, sameTileCost);
            }

            var portalSteps = FindPortalPath(startLoc, goalLoc, startXcm, startZcm, goalXcm, goalZcm, maxPortals, out var travelCost);
            if (portalSteps == null) return new NavPathResult(NavPathStatus.NotReachable, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);

            if (!TryBuildPortalTrianglePath(
                    startLoc,
                    goalLoc,
                    startXcm,
                    startZcm,
                    goalXcm,
                    goalZcm,
                    portalSteps,
                    maxPortals,
                    out int[] pathXcm,
                    out int[] pathZcm,
                    out travelCost))
            {
                return new NavPathResult(NavPathStatus.NotReachable, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }

            return new NavPathResult(NavPathStatus.Ok, pathXcm, pathZcm, travelCost);
        }

        /// <summary>
        /// Tile width/height in centimeters as Fix64 for deterministic tile location.
        /// Computed once from HexCoordinates constants * ChunkSize * 100 (cm conversion).
        /// </summary>
        private static readonly int DefaultTileWidthCm =
            (int)MathF.Round(HexCoordinates.HexWidth * VertexChunk.ChunkSize * 100f);
        private static readonly int DefaultTileHeightCm =
            (int)MathF.Round(HexCoordinates.RowSpacing * VertexChunk.ChunkSize * 100f);

        private NavTileId LocateTile(int worldXcm, int worldZcm)
        {
            int cx = FloorDiv(worldXcm - _worldMinXcm, _tileWidthCm);
            int cz = FloorDiv(worldZcm - _worldMinZcm, _tileHeightCm);
            if (cx < 0) cx = 0;
            if (cz < 0) cz = 0;
            return new NavTileId(cx, cz, _layer);
        }

        private int WorldToTileLocalX(NavTile tile, int worldXcm)
        {
            int runtimeLocalXcm = worldXcm - _worldMinXcm - GetRuntimeTileOriginXcm(tile);
            return ScaleCoordinate(runtimeLocalXcm, _tileWidthCm, _bakedTileWidthCm);
        }

        private int WorldToTileLocalZ(NavTile tile, int worldZcm)
        {
            int runtimeLocalZcm = worldZcm - _worldMinZcm - GetRuntimeTileOriginZcm(tile);
            return ScaleCoordinate(runtimeLocalZcm, _tileHeightCm, _bakedTileHeightCm);
        }

        private int TileLocalToWorldX(NavTile tile, int localXcm)
        {
            int runtimeLocalXcm = ScaleCoordinate(localXcm, _bakedTileWidthCm, _tileWidthCm);
            return _worldMinXcm + GetRuntimeTileOriginXcm(tile) + runtimeLocalXcm;
        }

        private int TileLocalToWorldZ(NavTile tile, int localZcm)
        {
            int runtimeLocalZcm = ScaleCoordinate(localZcm, _bakedTileHeightCm, _tileHeightCm);
            return _worldMinZcm + GetRuntimeTileOriginZcm(tile) + runtimeLocalZcm;
        }

        private int GetRuntimeTileOriginXcm(NavTile tile)
        {
            return tile.TileId.ChunkX * _tileWidthCm;
        }

        private int GetRuntimeTileOriginZcm(NavTile tile)
        {
            return tile.TileId.ChunkY * _tileHeightCm;
        }

        private static int ScaleCoordinate(int value, int fromExtentCm, int toExtentCm)
        {
            if (fromExtentCm <= 0 || toExtentCm <= 0 || fromExtentCm == toExtentCm)
            {
                return value;
            }

            long scaled = (long)value * toExtentCm;
            long half = fromExtentCm / 2;
            return scaled >= 0
                ? (int)((scaled + half) / fromExtentCm)
                : (int)((scaled - half) / fromExtentCm);
        }

        private static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            int r = value % divisor;
            if (r != 0 && ((r < 0) != (divisor < 0)))
            {
                q--;
            }

            return q;
        }

        private static int FindContainingTriangle(NavTile tile, int localXcm, int localZcm)
        {
            for (int i = 0; i < tile.TriangleCount; i++)
            {
                if (PointInTriangle(
                        localXcm,
                        localZcm,
                        tile.VertexXcm[tile.TriA[i]],
                        tile.VertexZcm[tile.TriA[i]],
                        tile.VertexXcm[tile.TriB[i]],
                        tile.VertexZcm[tile.TriB[i]],
                        tile.VertexXcm[tile.TriC[i]],
                        tile.VertexZcm[tile.TriC[i]]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindNearestTriangle(NavTile tile, int localXcm, int localZcm, int maxSurfaceDistanceCm = int.MaxValue)
        {
            int best = -1;
            long bestD2 = long.MaxValue;
            long maxD2 = maxSurfaceDistanceCm == int.MaxValue
                ? long.MaxValue
                : (long)Math.Max(0, maxSurfaceDistanceCm) * Math.Max(0, maxSurfaceDistanceCm);
            for (int i = 0; i < tile.TriangleCount; i++)
            {
                int a = tile.TriA[i];
                int b = tile.TriB[i];
                int c = tile.TriC[i];

                long d2 = DistanceSquaredToTriangle(
                    localXcm,
                    localZcm,
                    tile.VertexXcm[a],
                    tile.VertexZcm[a],
                    tile.VertexXcm[b],
                    tile.VertexZcm[b],
                    tile.VertexXcm[c],
                    tile.VertexZcm[c]);
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = i;
                }
            }

            return bestD2 <= maxD2 ? best : -1;
        }

        private bool TryBuildSameTileTrianglePath(
            NavTile tile,
            in NavLocation startLoc,
            in NavLocation goalLoc,
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            int maxPortals,
            out int[] pathXcm,
            out int[] pathZcm,
            out Fix64 travelCost)
        {
            pathXcm = Array.Empty<int>();
            pathZcm = Array.Empty<int>();
            travelCost = Fix64.Zero;

            if (startLoc.TriangleId < 0 ||
                goalLoc.TriangleId < 0 ||
                startLoc.TriangleId >= tile.TriangleCount ||
                goalLoc.TriangleId >= tile.TriangleCount)
            {
                return false;
            }

            if (startLoc.TriangleId == goalLoc.TriangleId)
            {
                pathXcm = new[] { startXcm, goalXcm };
                pathZcm = new[] { startZcm, goalZcm };
                travelCost = Dist(startXcm, startZcm, goalXcm, goalZcm) * GetTriangleCost(tile, startLoc.TriangleId);
                return true;
            }

            if (IsSegmentInsideTileNavMesh(tile, startLoc.LocalXcm, startLoc.LocalZcm, goalLoc.LocalXcm, goalLoc.LocalZcm))
            {
                pathXcm = new[] { startXcm, goalXcm };
                pathZcm = new[] { startZcm, goalZcm };
                travelCost = Dist(startXcm, startZcm, goalXcm, goalZcm) * GetTriangleCost(tile, startLoc.TriangleId);
                return true;
            }

            int maxTransitions = Math.Max(0, maxPortals);
            if (!TryFindTrianglePath(
                    tile,
                    startLoc.TriangleId,
                    goalLoc.TriangleId,
                    maxTransitions,
                    out int[] trianglePath))
            {
                return false;
            }

            int transitionCount = trianglePath.Length - 1;
            if (transitionCount <= 0 || transitionCount > maxTransitions)
            {
                return false;
            }

            int pointCount = transitionCount + 2;
            pathXcm = new int[pointCount];
            pathZcm = new int[pointCount];
            pathXcm[0] = startXcm;
            pathZcm[0] = startZcm;

            int previousX = startXcm;
            int previousZ = startZcm;
            for (int i = 0; i < transitionCount; i++)
            {
                if (!TryGetSharedEdgeMidpointWorld(
                        tile,
                        trianglePath[i],
                        trianglePath[i + 1],
                        out int midXcm,
                        out int midZcm))
                {
                    pathXcm = Array.Empty<int>();
                    pathZcm = Array.Empty<int>();
                    travelCost = Fix64.Zero;
                    return false;
                }

                int pathIndex = i + 1;
                pathXcm[pathIndex] = midXcm;
                pathZcm[pathIndex] = midZcm;
                travelCost += Dist(previousX, previousZ, midXcm, midZcm) * GetTriangleCost(tile, trianglePath[i]);
                previousX = midXcm;
                previousZ = midZcm;
            }

            pathXcm[^1] = goalXcm;
            pathZcm[^1] = goalZcm;
            travelCost += Dist(previousX, previousZ, goalXcm, goalZcm) * GetTriangleCost(tile, goalLoc.TriangleId);
            SimplifySameTilePath(tile, pathXcm, pathZcm, out int[] simplifiedXcm, out int[] simplifiedZcm);
            pathXcm = simplifiedXcm;
            pathZcm = simplifiedZcm;
            return true;
        }

        private bool TryFindTrianglePath(
            NavTile tile,
            int startTri,
            int goalTri,
            int maxTransitions,
            out int[] trianglePath)
        {
            trianglePath = Array.Empty<int>();
            if (maxTransitions <= 0)
            {
                return false;
            }

            int triCount = tile.TriangleCount;
            var open = new Fix64PriorityQueue<int>(Math.Min(256, Math.Max(16, triCount)));
            var closed = new bool[triCount];
            var parents = new int[triCount];
            var gScore = new Fix64[triCount];
            var depth = new int[triCount];
            Array.Fill(parents, -1);
            Array.Fill(gScore, Fix64.MaxValue);
            Array.Fill(depth, int.MaxValue);

            gScore[startTri] = Fix64.Zero;
            depth[startTri] = 0;
            open.Enqueue(startTri, EstimateTriangleHeuristic(tile, startTri, goalTri));

            while (open.TryDequeue(out int current, out _))
            {
                if ((uint)current >= (uint)triCount || closed[current])
                {
                    continue;
                }

                closed[current] = true;
                if (current == goalTri)
                {
                    return TryReconstructTrianglePath(parents, startTri, goalTri, maxTransitions, out trianglePath);
                }

                if (depth[current] >= maxTransitions)
                {
                    continue;
                }

                int[] neighbors = GetTriangleAdjacency(tile).Neighbors[current];
                for (int edge = 0; edge < neighbors.Length; edge++)
                {
                    int neighbor = neighbors[edge];
                    if ((uint)neighbor >= (uint)triCount || closed[neighbor])
                    {
                        continue;
                    }

                    int nextDepth = depth[current] + 1;
                    if (nextDepth > maxTransitions)
                    {
                        continue;
                    }

                    Fix64 candidateG = gScore[current] + ComputeTriangleStepCost(tile, current, neighbor);
                    if (candidateG >= gScore[neighbor] && nextDepth >= depth[neighbor])
                    {
                        continue;
                    }

                    gScore[neighbor] = candidateG;
                    depth[neighbor] = nextDepth;
                    parents[neighbor] = current;
                    open.Enqueue(neighbor, candidateG + EstimateTriangleHeuristic(tile, neighbor, goalTri));
                }
            }

            return false;
        }

        private static bool TryReconstructTrianglePath(
            int[] parents,
            int startTri,
            int goalTri,
            int maxTransitions,
            out int[] trianglePath)
        {
            var reversed = new List<int>(Math.Min(maxTransitions + 1, 256));
            int current = goalTri;
            while (current != startTri)
            {
                reversed.Add(current);
                if (reversed.Count > maxTransitions)
                {
                    trianglePath = Array.Empty<int>();
                    return false;
                }

                current = parents[current];
                if (current < 0)
                {
                    trianglePath = Array.Empty<int>();
                    return false;
                }
            }

            reversed.Add(startTri);
            reversed.Reverse();
            trianglePath = reversed.ToArray();
            return true;
        }

        private Fix64 ComputeTriangleStepCost(NavTile tile, int fromTri, int toTri)
        {
            GetTriangleCentroidLocal(tile, fromTri, out int fromX, out int fromZ);
            GetTriangleCentroidLocal(tile, toTri, out int toX, out int toZ);
            Fix64 cost = GetTriangleCost(tile, fromTri);
            Fix64 nextCost = GetTriangleCost(tile, toTri);
            if (nextCost > cost)
            {
                cost = nextCost;
            }

            return Dist(fromX, fromZ, toX, toZ) * cost;
        }

        private Fix64 EstimateTriangleHeuristic(NavTile tile, int fromTri, int goalTri)
        {
            GetTriangleCentroidLocal(tile, fromTri, out int fromX, out int fromZ);
            GetTriangleCentroidLocal(tile, goalTri, out int goalX, out int goalZ);
            return Dist(fromX, fromZ, goalX, goalZ) * _minCost;
        }

        private Fix64 GetTriangleCost(NavTile tile, int triId)
        {
            byte area = tile.TriAreaIds.Length > triId ? tile.TriAreaIds[triId] : (byte)0;
            return _areaCosts.Get(area);
        }

        private bool TryGetSharedEdgeMidpointWorld(
            NavTile tile,
            int fromTri,
            int toTri,
            out int xcm,
            out int zcm)
        {
            xcm = 0;
            zcm = 0;
            if ((uint)fromTri >= (uint)tile.TriangleCount || (uint)toTri >= (uint)tile.TriangleCount)
            {
                return false;
            }

            if (!TryGetSharedEdgeMidpointLocal(tile, fromTri, toTri, out int localX, out int localZ))
            {
                return false;
            }

            xcm = TileLocalToWorldX(tile, localX);
            zcm = TileLocalToWorldZ(tile, localZ);
            return true;
        }

        private TriangleAdjacency GetTriangleAdjacency(NavTile tile)
        {
            var key = new TriangleAdjacencyKey(tile.TileId, tile.TileVersion, tile.Checksum);
            lock (_triangleAdjacencySync)
            {
                if (_triangleAdjacencyCache.TryGetValue(key, out TriangleAdjacency cached))
                {
                    return cached;
                }
            }

            TriangleAdjacency built = BuildTriangleAdjacency(tile);
            lock (_triangleAdjacencySync)
            {
                if (_triangleAdjacencyCache.Count >= MaxTriangleAdjacencyCacheEntries)
                {
                    _triangleAdjacencyCache.Clear();
                }

                _triangleAdjacencyCache[key] = built;
            }

            return built;
        }

        private TriangleConnectivity GetTriangleConnectivity(NavTile tile)
        {
            var key = new TriangleAdjacencyKey(tile.TileId, tile.TileVersion, tile.Checksum);
            lock (_triangleConnectivitySync)
            {
                if (_triangleConnectivityCache.TryGetValue(key, out TriangleConnectivity cached))
                {
                    return cached;
                }
            }

            TriangleConnectivity built = BuildTriangleConnectivity(tile, GetTriangleAdjacency(tile));
            lock (_triangleConnectivitySync)
            {
                if (_triangleConnectivityCache.Count >= MaxTriangleConnectivityCacheEntries)
                {
                    _triangleConnectivityCache.Clear();
                }

                _triangleConnectivityCache[key] = built;
            }

            return built;
        }

        private static TriangleConnectivity BuildTriangleConnectivity(NavTile tile, TriangleAdjacency adjacency)
        {
            int triCount = tile.TriangleCount;
            var components = new int[triCount];
            Array.Fill(components, -1);
            if (triCount == 0)
            {
                return new TriangleConnectivity(components);
            }

            var queue = new Queue<int>(triCount);
            int componentId = 0;
            for (int tri = 0; tri < triCount; tri++)
            {
                if (components[tri] >= 0)
                {
                    continue;
                }

                components[tri] = componentId;
                queue.Clear();
                queue.Enqueue(tri);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int[] neighbors = adjacency.Neighbors[current];
                    for (int i = 0; i < neighbors.Length; i++)
                    {
                        int neighbor = neighbors[i];
                        if ((uint)neighbor >= (uint)triCount || components[neighbor] >= 0)
                        {
                            continue;
                        }

                        components[neighbor] = componentId;
                        queue.Enqueue(neighbor);
                    }
                }

                componentId++;
            }

            return new TriangleConnectivity(components);
        }

        private bool AreTrianglesConnected(NavTile tile, int startTri, int goalTri)
        {
            if ((uint)startTri >= (uint)tile.TriangleCount ||
                (uint)goalTri >= (uint)tile.TriangleCount)
            {
                return false;
            }

            if (startTri == goalTri)
            {
                return true;
            }

            TriangleConnectivity connectivity = GetTriangleConnectivity(tile);
            return startTri < connectivity.Components.Length &&
                goalTri < connectivity.Components.Length &&
                connectivity.Components[startTri] >= 0 &&
                connectivity.Components[startTri] == connectivity.Components[goalTri];
        }

        private static TriangleAdjacency BuildTriangleAdjacency(NavTile tile)
        {
            int triCount = tile.TriangleCount;
            var neighbors = new List<int>[triCount];
            for (int i = 0; i < triCount; i++)
            {
                neighbors[i] = new List<int>(3);
                AddStoredTriangleNeighbor(tile, neighbors[i], i, 0);
                AddStoredTriangleNeighbor(tile, neighbors[i], i, 1);
                AddStoredTriangleNeighbor(tile, neighbors[i], i, 2);
            }

            for (int a = 0; a < triCount; a++)
            {
                for (int b = a + 1; b < triCount; b++)
                {
                    if (ContainsNeighbor(neighbors[a], b))
                    {
                        continue;
                    }

                    if (!TryGetSharedEdgeMidpointLocal(tile, a, b, out _, out _))
                    {
                        continue;
                    }

                    neighbors[a].Add(b);
                    neighbors[b].Add(a);
                }
            }

            var packed = new int[triCount][];
            for (int i = 0; i < triCount; i++)
            {
                packed[i] = neighbors[i].ToArray();
            }

            return new TriangleAdjacency(packed);
        }

        private static void AddStoredTriangleNeighbor(NavTile tile, List<int> neighbors, int triId, int edge)
        {
            int neighbor = GetTriangleNeighbor(tile, triId, edge);
            if ((uint)neighbor >= (uint)tile.TriangleCount || neighbor == triId || ContainsNeighbor(neighbors, neighbor))
            {
                return;
            }

            neighbors.Add(neighbor);
        }

        private static bool ContainsNeighbor(List<int> neighbors, int candidate)
        {
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (neighbors[i] == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetSharedEdgeMidpointLocal(NavTile tile, int fromTri, int toTri, out int xcm, out int zcm)
        {
            xcm = 0;
            zcm = 0;
            if ((uint)fromTri >= (uint)tile.TriangleCount || (uint)toTri >= (uint)tile.TriangleCount)
            {
                return false;
            }

            int bestScore = -1;
            for (int fromEdge = 0; fromEdge < 3; fromEdge++)
            {
                GetTriangleEdgeVertices(tile, fromTri, fromEdge, out int fa, out int fb);
                int fax = tile.VertexXcm[fa];
                int faz = tile.VertexZcm[fa];
                int fbx = tile.VertexXcm[fb];
                int fbz = tile.VertexZcm[fb];

                for (int toEdge = 0; toEdge < 3; toEdge++)
                {
                    GetTriangleEdgeVertices(tile, toTri, toEdge, out int ta, out int tb);
                    int tax = tile.VertexXcm[ta];
                    int taz = tile.VertexZcm[ta];
                    int tbx = tile.VertexXcm[tb];
                    int tbz = tile.VertexZcm[tb];

                    if (!TryGetSegmentOverlapMidpoint(
                            fax,
                            faz,
                            fbx,
                            fbz,
                            tax,
                            taz,
                            tbx,
                            tbz,
                            out int overlapX,
                            out int overlapZ,
                            out int overlapScore))
                    {
                        continue;
                    }

                    if (overlapScore > bestScore)
                    {
                        xcm = overlapX;
                        zcm = overlapZ;
                        bestScore = overlapScore;
                    }
                }
            }

            return bestScore >= 0;
        }

        private static void GetTriangleEdgeVertices(NavTile tile, int triId, int edge, out int va, out int vb)
        {
            if (edge == 0)
            {
                va = tile.TriA[triId];
                vb = tile.TriB[triId];
                return;
            }

            if (edge == 1)
            {
                va = tile.TriB[triId];
                vb = tile.TriC[triId];
                return;
            }

            va = tile.TriC[triId];
            vb = tile.TriA[triId];
        }

        private static bool TryGetSegmentOverlapMidpoint(
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz,
            int dx,
            int dz,
            out int midX,
            out int midZ,
            out int overlapScore)
        {
            midX = 0;
            midZ = 0;
            overlapScore = 0;

            long abx = (long)bx - ax;
            long abz = (long)bz - az;
            long lenSq = (abx * abx) + (abz * abz);
            if (lenSq <= 0)
            {
                return false;
            }

            long tolerance = GeometricEdgeToleranceCm;
            if (DistanceSquaredToLine(cx, cz, ax, az, abx, abz, lenSq) > tolerance * tolerance ||
                DistanceSquaredToLine(dx, dz, ax, az, abx, abz, lenSq) > tolerance * tolerance)
            {
                return false;
            }

            long cProjection = (((long)cx - ax) * abx) + (((long)cz - az) * abz);
            long dProjection = (((long)dx - ax) * abx) + (((long)dz - az) * abz);
            long otherStart = Math.Min(cProjection, dProjection);
            long otherEnd = Math.Max(cProjection, dProjection);
            long overlapStart = Math.Max(0, otherStart);
            long overlapEnd = Math.Min(lenSq, otherEnd);
            long overlap = overlapEnd - overlapStart;
            if (overlap <= 0 ||
                (overlap * overlap) < (long)MinSharedEdgeOverlapCm * MinSharedEdgeOverlapCm * lenSq)
            {
                return false;
            }

            long midProjection = overlapStart + (overlap / 2);
            midX = ax + (int)DivRound(abx * midProjection, lenSq);
            midZ = az + (int)DivRound(abz * midProjection, lenSq);
            overlapScore = overlap > int.MaxValue ? int.MaxValue : (int)overlap;
            return true;
        }

        private static long DistanceSquaredToLine(
            int px,
            int pz,
            int ax,
            int az,
            long abx,
            long abz,
            long lenSq)
        {
            long cross = (((long)px - ax) * abz) - (((long)pz - az) * abx);
            if (cross == 0)
            {
                return 0;
            }

            return DivRound(cross * cross, lenSq);
        }

        private static int GetTriangleNeighbor(NavTile tile, int triId, int edge)
        {
            return edge switch
            {
                0 => tile.N0[triId],
                1 => tile.N1[triId],
                2 => tile.N2[triId],
                _ => -1
            };
        }

        private static void GetTriangleCentroidLocal(NavTile tile, int triId, out int xcm, out int zcm)
        {
            int a = tile.TriA[triId];
            int b = tile.TriB[triId];
            int c = tile.TriC[triId];
            xcm = (tile.VertexXcm[a] + tile.VertexXcm[b] + tile.VertexXcm[c]) / 3;
            zcm = (tile.VertexZcm[a] + tile.VertexZcm[b] + tile.VertexZcm[c]) / 3;
        }

        private static bool PointInTriangle(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            long area = Orient2D(ax, az, bx, bz, cx, cz);
            if (area == 0)
            {
                return false;
            }

            long ab = Orient2D(ax, az, bx, bz, px, pz);
            long bc = Orient2D(bx, bz, cx, cz, px, pz);
            long ca = Orient2D(cx, cz, ax, az, px, pz);
            return area > 0
                ? ab >= 0 && bc >= 0 && ca >= 0
                : ab <= 0 && bc <= 0 && ca <= 0;
        }

        private static long DistanceSquaredToTriangle(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            if (PointInTriangle(px, pz, ax, az, bx, bz, cx, cz))
            {
                return 0;
            }

            long ab = DistanceSquaredToSegment(px, pz, ax, az, bx, bz);
            long bc = DistanceSquaredToSegment(px, pz, bx, bz, cx, cz);
            long ca = DistanceSquaredToSegment(px, pz, cx, cz, ax, az);
            return Math.Min(ab, Math.Min(bc, ca));
        }

        private static long DistanceSquaredToSegment(int px, int pz, int ax, int az, int bx, int bz)
        {
            long dx = bx - ax;
            long dz = bz - az;
            long len2 = (dx * dx) + (dz * dz);
            if (len2 <= 0)
            {
                return DistanceSquared(px, pz, ax, az);
            }

            long pax = px - ax;
            long paz = pz - az;
            long dot = (pax * dx) + (paz * dz);
            if (dot <= 0)
            {
                return DistanceSquared(px, pz, ax, az);
            }

            if (dot >= len2)
            {
                return DistanceSquared(px, pz, bx, bz);
            }

            long cross = (pax * dz) - (paz * dx);
            double d2 = (double)cross * cross / len2;
            return (long)Math.Ceiling(d2);
        }

        private static long DistanceSquared(int ax, int az, int bx, int bz)
        {
            long dx = (long)bx - ax;
            long dz = (long)bz - az;
            return (dx * dx) + (dz * dz);
        }

        private static long Orient2D(int ax, int az, int bx, int bz, int cx, int cz)
        {
            return ((long)bx - ax) * ((long)cz - az) - (((long)bz - az) * ((long)cx - ax));
        }

        private sealed class Node
        {
            public PortalRef Ref;
            public PortalWorldSeg Seg;
            public bool HasSeg;
            public Fix64 G;
            public Fix64 F;
            public int Prev;
        }

        private readonly struct PortalRef : IEquatable<PortalRef>
        {
            public readonly NavTileId TileId;
            public readonly int PortalIndex;

            public PortalRef(NavTileId tileId, int portalIndex)
            {
                TileId = tileId;
                PortalIndex = portalIndex;
            }

            public bool Equals(PortalRef other) => TileId.Equals(other.TileId) && PortalIndex == other.PortalIndex;
            public override bool Equals(object obj) => obj is PortalRef other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(TileId, PortalIndex);
        }

        private readonly struct PortalWorldSeg
        {
            public readonly int Ax;
            public readonly int Az;
            public readonly int Bx;
            public readonly int Bz;

            public PortalWorldSeg(int ax, int az, int bx, int bz)
            {
                Ax = ax;
                Az = az;
                Bx = bx;
                Bz = bz;
            }
        }

        private readonly struct TilePortalStep
        {
            public readonly NavTileId FromTileId;
            public readonly NavTileId ToTileId;
            public readonly PortalWorldSeg Portal;
            public readonly int ExitWorldXcm;
            public readonly int ExitWorldZcm;
            public readonly int EntryWorldXcm;
            public readonly int EntryWorldZcm;

            public TilePortalStep(
                NavTileId fromTileId,
                NavTileId toTileId,
                PortalWorldSeg portal,
                int exitWorldXcm,
                int exitWorldZcm,
                int entryWorldXcm,
                int entryWorldZcm)
            {
                FromTileId = fromTileId;
                ToTileId = toTileId;
                Portal = portal;
                ExitWorldXcm = exitWorldXcm;
                ExitWorldZcm = exitWorldZcm;
                EntryWorldXcm = entryWorldXcm;
                EntryWorldZcm = entryWorldZcm;
            }
        }

        private readonly struct TilePathParent
        {
            public readonly NavTileId PreviousTileId;
            public readonly PortalWorldSeg PortalFromPrevious;

            public TilePathParent(NavTileId previousTileId, PortalWorldSeg portalFromPrevious)
            {
                PreviousTileId = previousTileId;
                PortalFromPrevious = portalFromPrevious;
            }
        }

        private readonly struct PortalStateKey : IEquatable<PortalStateKey>
        {
            public readonly NavTileId TileId;
            public readonly int EntryPortalIndex;
            public readonly PortalKey EntryPortalKey;

            public PortalStateKey(NavTileId tileId, int entryPortalIndex, PortalWorldSeg entryPortal)
            {
                TileId = tileId;
                EntryPortalIndex = entryPortalIndex;
                EntryPortalKey = entryPortalIndex >= 0
                    ? new PortalKey(entryPortal)
                    : default;
            }

            public bool Equals(PortalStateKey other)
            {
                return TileId.Equals(other.TileId) &&
                    EntryPortalIndex == other.EntryPortalIndex &&
                    EntryPortalKey.Equals(other.EntryPortalKey);
            }

            public override bool Equals(object obj) => obj is PortalStateKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(TileId, EntryPortalIndex, EntryPortalKey);
        }

        private readonly struct PortalSearchState
        {
            public readonly NavLocation Location;
            public readonly int WorldXcm;
            public readonly int WorldZcm;

            public PortalSearchState(NavLocation location, int worldXcm, int worldZcm)
            {
                Location = location;
                WorldXcm = worldXcm;
                WorldZcm = worldZcm;
            }
        }

        private readonly struct PortalStateParent
        {
            public readonly PortalStateKey PreviousKey;
            public readonly TilePortalStep StepFromPrevious;

            public PortalStateParent(PortalStateKey previousKey, TilePortalStep stepFromPrevious)
            {
                PreviousKey = previousKey;
                StepFromPrevious = stepFromPrevious;
            }
        }

        private readonly struct PortalKey : IEquatable<PortalKey>
        {
            // Tolerance in cm for matching portals (handles floating-point rounding)
            private const int Tolerance = 2;

            public readonly int Ax;
            public readonly int Az;
            public readonly int Bx;
            public readonly int Bz;

            public PortalKey(PortalWorldSeg seg)
                : this(seg.Ax, seg.Az, seg.Bx, seg.Bz)
            {
            }

            public PortalKey(int ax, int az, int bx, int bz)
            {
                // Quantize to tolerance grid to handle rounding differences
                ax = (ax / Tolerance) * Tolerance;
                az = (az / Tolerance) * Tolerance;
                bx = (bx / Tolerance) * Tolerance;
                bz = (bz / Tolerance) * Tolerance;

                if (ax < bx || (ax == bx && az <= bz))
                {
                    Ax = ax; Az = az; Bx = bx; Bz = bz;
                }
                else
                {
                    Ax = bx; Az = bz; Bx = ax; Bz = az;
                }
            }

            public bool Equals(PortalKey other) => Ax == other.Ax && Az == other.Az && Bx == other.Bx && Bz == other.Bz;
            public override bool Equals(object obj) => obj is PortalKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Ax, Az, Bx, Bz);
        }

        private List<TilePortalStep> FindPortalPath(
            in NavLocation startLoc,
            in NavLocation goalLoc,
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            int maxPortals,
            out Fix64 travelCost)
        {
            travelCost = Fix64.Zero;
            if (maxPortals <= 0)
            {
                return null;
            }

            if (!TryFindTilePortalPath(
                    startLoc,
                    goalLoc,
                    startXcm,
                    startZcm,
                    goalXcm,
                    goalZcm,
                    maxPortals,
                    out List<TilePortalStep> portals))
            {
                travelCost = Fix64.Zero;
                return null;
            }

            travelCost = ComputePortalPathTravelCost(startXcm, startZcm, goalXcm, goalZcm, portals);
            return portals;
        }

        private bool TryFindTilePortalPath(
            in NavLocation startLoc,
            in NavLocation goalLoc,
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            int maxPortals,
            out List<TilePortalStep> portals)
        {
            portals = null;
            var open = new Fix64PriorityQueue<PortalStateKey>(Math.Min(4096, Math.Max(64, maxPortals * 4)));
            var gScore = new Dictionary<PortalStateKey, Fix64>(Math.Min(4096, Math.Max(64, maxPortals * 4)));
            var parents = new Dictionary<PortalStateKey, PortalStateParent>(Math.Min(4096, Math.Max(64, maxPortals * 4)));
            var states = new Dictionary<PortalStateKey, PortalSearchState>(Math.Min(4096, Math.Max(64, maxPortals * 4)));
            var closed = new HashSet<PortalStateKey>();
            int expansionBudget = ResolveTileExpansionBudget(startLoc.TileId, goalLoc.TileId, maxPortals);
            var startKey = new PortalStateKey(startLoc.TileId, -1, default);

            gScore[startKey] = Fix64.Zero;
            states[startKey] = new PortalSearchState(startLoc, startXcm, startZcm);
            open.Enqueue(startKey, EstimateTileHeuristic(startLoc.TileId, goalLoc.TileId));

            int expanded = 0;
            while (open.TryDequeue(out PortalStateKey currentKey, out _))
            {
                if (!closed.Add(currentKey) || !states.TryGetValue(currentKey, out PortalSearchState currentState))
                {
                    continue;
                }

                expanded++;
                if (expanded > expansionBudget)
                {
                    return false;
                }

                NavTile currentTile;
                try
                {
                    currentTile = _store.GetOrLoad(currentKey.TileId);
                }
                catch
                {
                    continue;
                }

                if (!gScore.TryGetValue(currentKey, out Fix64 currentG))
                {
                    continue;
                }

                if (currentKey.TileId.Equals(goalLoc.TileId) &&
                    TryCanReachPointInTile(
                        currentTile,
                        currentState.Location.TriangleId,
                        currentState.WorldXcm,
                        currentState.WorldZcm,
                        goalXcm,
                        goalZcm,
                        maxPortals,
                        out _,
                        out Fix64 goalCost))
                {
                    return TryReconstructTilePortalPath(startKey, currentKey, parents, maxPortals, out portals);
                }

                for (int i = 0; i < currentTile.Portals.Length; i++)
                {
                    NavTileId neighborId = GetNeighborTileId(currentKey.TileId, currentTile.Portals[i].Side);
                    if (neighborId.ChunkX < 0 || neighborId.ChunkY < 0)
                    {
                        continue;
                    }

                    NavTile neighborTile;
                    try
                    {
                        neighborTile = _store.GetOrLoad(neighborId);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!TryResolveNeighborPortal(currentTile, i, neighborTile, out int neighborPortalIndex, out PortalWorldSeg crossingPortal))
                    {
                        continue;
                    }

                    if (!TryResolveReachablePortalCrossing(
                            currentTile,
                            currentState.Location.TriangleId,
                            currentState.WorldXcm,
                            currentState.WorldZcm,
                            neighborTile,
                            neighborPortalIndex,
                            crossingPortal,
                            maxPortals,
                            out NavLocation nextLoc,
                            out int exitXcm,
                            out int exitZcm,
                            out int nextXcm,
                            out int nextZcm,
                            out Fix64 stepCost))
                    {
                        continue;
                    }

                    var neighborKey = new PortalStateKey(
                        neighborId,
                        neighborPortalIndex,
                        new PortalWorldSeg(nextXcm, nextZcm, nextXcm, nextZcm));
                    if (closed.Contains(neighborKey))
                    {
                        continue;
                    }

                    Fix64 candidateG = currentG + stepCost;
                    if (gScore.TryGetValue(neighborKey, out Fix64 previousG) && candidateG >= previousG)
                    {
                        continue;
                    }

                    gScore[neighborKey] = candidateG;
                    states[neighborKey] = new PortalSearchState(nextLoc, nextXcm, nextZcm);
                    parents[neighborKey] = new PortalStateParent(
                        currentKey,
                        new TilePortalStep(
                            currentKey.TileId,
                            neighborId,
                            crossingPortal,
                            exitXcm,
                            exitZcm,
                            nextXcm,
                            nextZcm));
                    open.Enqueue(neighborKey, candidateG + EstimateTileHeuristic(neighborId, goalLoc.TileId));
                }
            }

            return false;
        }

        private static int ResolveTileExpansionBudget(NavTileId startTileId, NavTileId goalTileId, int maxPortals)
        {
            int direct = Math.Abs(goalTileId.ChunkX - startTileId.ChunkX) +
                Math.Abs(goalTileId.ChunkY - startTileId.ChunkY) +
                1;
            int budget = Math.Max(4096, Math.Max(direct * 16, maxPortals * 64));
            return Math.Min(262_144, budget);
        }

        private Fix64 EstimateTileHeuristic(NavTileId from, NavTileId goal)
        {
            int dx = Math.Abs(goal.ChunkX - from.ChunkX) * _tileWidthCm;
            int dz = Math.Abs(goal.ChunkY - from.ChunkY) * _tileHeightCm;
            return Fix64.FromInt(dx + dz) * _minCost;
        }

        private bool TryReconstructTilePortalPath(
            PortalStateKey startKey,
            PortalStateKey goalKey,
            Dictionary<PortalStateKey, PortalStateParent> parents,
            int maxPortals,
            out List<TilePortalStep> portals)
        {
            var reversed = new List<TilePortalStep>(Math.Min(maxPortals, 128));
            PortalStateKey current = goalKey;
            while (!current.Equals(startKey))
            {
                if (!parents.TryGetValue(current, out PortalStateParent parent))
                {
                    portals = null;
                    return false;
                }

                reversed.Add(parent.StepFromPrevious);
                if (reversed.Count > maxPortals)
                {
                    portals = null;
                    return false;
                }

                current = parent.PreviousKey;
            }

            reversed.Reverse();
            portals = reversed;
            return true;
        }

        private Fix64 ComputeTileStepCost(
            NavTile currentTile,
            int currentPortalIndex,
            NavTile neighborTile,
            int neighborPortalIndex)
        {
            int currentX = GetTileCenterWorldXcm(currentTile);
            int currentZ = GetTileCenterWorldZcm(currentTile);
            int neighborX = GetTileCenterWorldXcm(neighborTile);
            int neighborZ = GetTileCenterWorldZcm(neighborTile);
            Fix64 currentCost = PortalCost(currentTile, currentPortalIndex);
            Fix64 neighborCost = PortalCost(neighborTile, neighborPortalIndex);
            Fix64 areaCost = currentCost < neighborCost ? neighborCost : currentCost;
            return Dist(currentX, currentZ, neighborX, neighborZ) * areaCost;
        }

        private Fix64 ComputePortalPathTravelCost(
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            IReadOnlyList<TilePortalStep> portals)
        {
            int prevX = startXcm;
            int prevZ = startZcm;
            Fix64 cost = Fix64.Zero;
            for (int i = 0; i < portals.Count; i++)
            {
                TilePortalStep step = portals[i];
                cost += Dist(prevX, prevZ, step.ExitWorldXcm, step.ExitWorldZcm) * _minCost;
                cost += Dist(step.ExitWorldXcm, step.ExitWorldZcm, step.EntryWorldXcm, step.EntryWorldZcm) * _minCost;
                prevX = step.EntryWorldXcm;
                prevZ = step.EntryWorldZcm;
            }

            cost += Dist(prevX, prevZ, goalXcm, goalZcm) * _minCost;
            return cost;
        }

        private static int AddStartVirtualNode(List<Node> nodes)
        {
            var r = new PortalRef(new NavTileId(-1, -1, 0), -1);
            nodes.Add(new Node { Ref = r, HasSeg = false, G = Fix64.Zero, F = Fix64.Zero, Prev = -1 });
            return 0;
        }

        private static int AddGoalVirtualNode(List<Node> nodes)
        {
            var r = new PortalRef(new NavTileId(-2, -2, 0), -2);
            nodes.Add(new Node { Ref = r, HasSeg = false, G = Fix64.Zero, F = Fix64.Zero, Prev = -1 });
            return 1;
        }

        private int GetOrCreateNode(PortalRef pref, NavTile tile, List<Node> nodes, Dictionary<PortalRef, int> nodeIndex)
        {
            if (nodeIndex.TryGetValue(pref, out int id)) return id;
            id = nodes.Count;
            nodes.Add(new Node { Ref = pref, Seg = GetPortalWorldSeg(tile, pref.PortalIndex), HasSeg = true, G = Fix64.MaxValue, F = Fix64.MaxValue, Prev = -1 });
            nodeIndex[pref] = id;
            return id;
        }

        /// <summary>
        /// Deterministic integer-safe Euclidean distance in centimeters.
        /// Uses long arithmetic for dx*dx to avoid Fix64 multiplication overflow
        /// (Fix64 overflows when |dx| > ~46340). The result is converted to Fix64
        /// after integer sqrt, losing sub-cm precision — acceptable for A* heuristic.
        /// </summary>
        private static Fix64 Dist(int ax, int az, int bx, int bz)
        {
            long dx = (long)(bx - ax);
            long dz = (long)(bz - az);
            long distSq = dx * dx + dz * dz;
            long dist = DeterministicLongSqrt(distSq);
            return Fix64.FromInt((int)dist);
        }

        /// <summary>
        /// Pure-integer Newton's method square root. Deterministic across all platforms.
        /// No floating-point used — safe for lockstep/replay.
        /// </summary>
        private static long DeterministicLongSqrt(long n)
        {
            if (n <= 0) return 0;
            if (n == 1) return 1;

            // Newton's method with integer arithmetic
            long x = n;
            long y = (x + 1) >> 1;
            while (y < x)
            {
                x = y;
                y = (x + n / x) >> 1;
            }
            return x;
        }

        private static NavTileId GetNeighborTileId(NavTileId id, NavPortalSide side)
        {
            return side switch
            {
                NavPortalSide.West => new NavTileId(id.ChunkX - 1, id.ChunkY, id.Layer),
                NavPortalSide.East => new NavTileId(id.ChunkX + 1, id.ChunkY, id.Layer),
                NavPortalSide.North => new NavTileId(id.ChunkX, id.ChunkY - 1, id.Layer),
                NavPortalSide.South => new NavTileId(id.ChunkX, id.ChunkY + 1, id.Layer),
                _ => id
            };
        }

        private bool TryResolveNeighborPortal(
            NavTile tile,
            int portalIndex,
            NavTile neighborTile,
            out int neighborPortalIndex,
            out PortalWorldSeg crossingPortal)
        {
            NavBorderPortal portal = tile.Portals[portalIndex];
            NavPortalSide opposite = GetOppositeSide(portal.Side);
            GetPortalInterval(portal, out int portalStart, out int portalEnd);
            neighborPortalIndex = -1;
            int bestOverlapStart = 0;
            int bestOverlapEnd = 0;
            int bestOverlapLength = 0;
            for (int i = 0; i < neighborTile.Portals.Length; i++)
            {
                NavBorderPortal neighborPortal = neighborTile.Portals[i];
                if (neighborPortal.Side != opposite)
                {
                    continue;
                }

                GetPortalInterval(neighborPortal, out int neighborStart, out int neighborEnd);
                int overlapStart = Math.Max(portalStart, neighborStart);
                int overlapEnd = Math.Min(portalEnd, neighborEnd);
                int overlapLength = overlapEnd - overlapStart;
                if (overlapLength <= 0 || overlapLength <= bestOverlapLength)
                {
                    continue;
                }

                bestOverlapStart = overlapStart;
                bestOverlapEnd = overlapEnd;
                bestOverlapLength = overlapLength;
                neighborPortalIndex = i;
            }

            if (bestOverlapLength <= 0)
            {
                neighborPortalIndex = -1;
                crossingPortal = default;
                return false;
            }

            crossingPortal = BuildPortalOverlapWorldSeg(tile, portal, bestOverlapStart, bestOverlapEnd);
            return true;
        }

        private static void GetPortalInterval(NavBorderPortal portal, out int start, out int end)
        {
            if (portal.Side == NavPortalSide.West || portal.Side == NavPortalSide.East)
            {
                start = Math.Min(portal.V0, portal.V1);
                end = Math.Max(portal.V0, portal.V1);
            }
            else
            {
                start = Math.Min(portal.U0, portal.U1);
                end = Math.Max(portal.U0, portal.U1);
            }
        }

        private static NavPortalSide GetOppositeSide(NavPortalSide side)
        {
            return side switch
            {
                NavPortalSide.West => NavPortalSide.East,
                NavPortalSide.East => NavPortalSide.West,
                NavPortalSide.North => NavPortalSide.South,
                NavPortalSide.South => NavPortalSide.North,
                _ => side
            };
        }

        private PortalWorldSeg BuildPortalOverlapWorldSeg(
            NavTile tile,
            NavBorderPortal portal,
            int overlapStart,
            int overlapEnd)
        {
            int ax = InterpolatePortalLocalX(portal, overlapStart);
            int az = InterpolatePortalLocalZ(portal, overlapStart);
            int bx = InterpolatePortalLocalX(portal, overlapEnd);
            int bz = InterpolatePortalLocalZ(portal, overlapEnd);
            return new PortalWorldSeg(
                TileLocalToWorldX(tile, ax),
                TileLocalToWorldZ(tile, az),
                TileLocalToWorldX(tile, bx),
                TileLocalToWorldZ(tile, bz));
        }

        private static int InterpolatePortalLocalX(NavBorderPortal portal, int intervalValue)
        {
            GetPortalRawInterval(portal, out int start, out int end);
            return InterpolateInt(portal.LeftXcm, portal.RightXcm, start, end, intervalValue);
        }

        private static int InterpolatePortalLocalZ(NavBorderPortal portal, int intervalValue)
        {
            GetPortalRawInterval(portal, out int start, out int end);
            return InterpolateInt(portal.LeftZcm, portal.RightZcm, start, end, intervalValue);
        }

        private static void GetPortalRawInterval(NavBorderPortal portal, out int start, out int end)
        {
            if (portal.Side == NavPortalSide.West || portal.Side == NavPortalSide.East)
            {
                start = portal.V0;
                end = portal.V1;
            }
            else
            {
                start = portal.U0;
                end = portal.U1;
            }
        }

        private static int InterpolateInt(int startValue, int endValue, int start, int end, int value)
        {
            int span = end - start;
            if (span == 0)
            {
                return startValue;
            }

            long numerator = (long)(endValue - startValue) * (value - start);
            long half = Math.Abs(span) / 2;
            long rounded = numerator >= 0
                ? (numerator + half) / span
                : (numerator - half) / span;
            return startValue + (int)rounded;
        }

        private int GetTileCenterWorldXcm(NavTile tile)
        {
            return _worldMinXcm + GetRuntimeTileOriginXcm(tile) + (_tileWidthCm / 2);
        }

        private int GetTileCenterWorldZcm(NavTile tile)
        {
            return _worldMinZcm + GetRuntimeTileOriginZcm(tile) + (_tileHeightCm / 2);
        }

        private PortalWorldSeg GetPortalWorldSeg(NavTile tile, int portalIndex)
        {
            var p = tile.Portals[portalIndex];
            int ax = TileLocalToWorldX(tile, p.LeftXcm);
            int az = TileLocalToWorldZ(tile, p.LeftZcm);
            int bx = TileLocalToWorldX(tile, p.RightXcm);
            int bz = TileLocalToWorldZ(tile, p.RightZcm);
            return new PortalWorldSeg(ax, az, bx, bz);
        }

        private int GetPortalMidWorldXcm(NavTile tile, int portalIndex)
        {
            var seg = GetPortalWorldSeg(tile, portalIndex);
            return (seg.Ax + seg.Bx) / 2;
        }

        private int GetPortalMidWorldZcm(NavTile tile, int portalIndex)
        {
            var seg = GetPortalWorldSeg(tile, portalIndex);
            return (seg.Az + seg.Bz) / 2;
        }

        private Fix64 PortalCost(NavTile tile, int portalIndex)
        {
            if (tile.TriangleCount == 0) return Fix64.OneValue;
            int wx = GetPortalMidWorldXcm(tile, portalIndex);
            int wz = GetPortalMidWorldZcm(tile, portalIndex);
            int localX = WorldToTileLocalX(tile, wx);
            int localZ = WorldToTileLocalZ(tile, wz);
            int triId = FindNearestTriangle(tile, localX, localZ);
            if (triId < 0) return Fix64.OneValue;
            return _areaCosts.Get(tile.TriAreaIds[triId]);
        }

        private static List<PortalWorldSeg> Reconstruct(List<Node> nodes, int goalNode, int startNode, int maxPortals)
        {
            var rev = new List<PortalWorldSeg>(64);
            int cur = nodes[goalNode].Prev;
            while (cur != startNode && cur >= 0 && rev.Count < maxPortals)
            {
                if (nodes[cur].HasSeg) rev.Add(nodes[cur].Seg);
                cur = nodes[cur].Prev;
            }
            rev.Reverse();
            return rev;
        }

        private bool TryBuildPortalTrianglePath(
            in NavLocation startLoc,
            in NavLocation goalLoc,
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            IReadOnlyList<TilePortalStep> pathPortals,
            int maxPortals,
            out int[] pathXcm,
            out int[] pathZcm,
            out Fix64 travelCost)
        {
            pathXcm = Array.Empty<int>();
            pathZcm = Array.Empty<int>();
            travelCost = Fix64.Zero;
            if (pathPortals == null || pathPortals.Count == 0 || maxPortals <= 0)
            {
                return false;
            }

            int maxPoints = Math.Max(2, (maxPortals * 2) + 2);
            var xs = new List<int>(Math.Min(maxPoints, pathPortals.Count + 2)) { startXcm };
            var zs = new List<int>(Math.Min(maxPoints, pathPortals.Count + 2)) { startZcm };
            NavTileId currentTileId = startLoc.TileId;
            int currentTri = startLoc.TriangleId;
            int currentX = startXcm;
            int currentZ = startZcm;

            for (int i = 0; i < pathPortals.Count; i++)
            {
                NavTile currentTile;
                try
                {
                    currentTile = _store.GetOrLoad(currentTileId);
                }
                catch
                {
                    return false;
                }

                TilePortalStep step = pathPortals[i];
                if (!step.FromTileId.Equals(currentTileId))
                {
                    return false;
                }

                if (!TryAppendTileSegment(
                        currentTile,
                        currentTri,
                        currentX,
                        currentZ,
                        step.ExitWorldXcm,
                        step.ExitWorldZcm,
                        maxPoints,
                        xs,
                        zs,
                        ref travelCost,
                        out _))
                {
                    return false;
                }

                NavTile nextTile;
                try
                {
                    nextTile = _store.GetOrLoad(step.ToTileId);
                }
                catch
                {
                    return false;
                }

                currentX = step.EntryWorldXcm;
                currentZ = step.EntryWorldZcm;
                if (!TryProjectIntoTile(nextTile, currentX, currentZ, out NavLocation nextLoc))
                {
                    return false;
                }

                if (!TryAppendPoint(xs, zs, currentX, currentZ, maxPoints))
                {
                    return false;
                }

                currentTileId = step.ToTileId;
                currentTri = nextLoc.TriangleId;
            }

            if (!currentTileId.Equals(goalLoc.TileId))
            {
                return false;
            }

            NavTile goalTile;
            try
            {
                goalTile = _store.GetOrLoad(goalLoc.TileId);
            }
            catch
            {
                return false;
            }

            if (!TryAppendTileSegment(
                    goalTile,
                    currentTri,
                    currentX,
                    currentZ,
                    goalXcm,
                    goalZcm,
                    maxPoints,
                    xs,
                    zs,
                    ref travelCost,
                    out _))
            {
                return false;
            }

            if (xs.Count < 2 || xs.Count != zs.Count)
            {
                return false;
            }

            pathXcm = xs.ToArray();
            pathZcm = zs.ToArray();
            pathXcm[0] = startXcm;
            pathZcm[0] = startZcm;
            pathXcm[^1] = goalXcm;
            pathZcm[^1] = goalZcm;
            return true;
        }

        private bool TryResolveReachablePortalCrossing(
            NavTile currentTile,
            int startTri,
            int startXcm,
            int startZcm,
            NavTile neighborTile,
            int neighborPortalIndex,
            PortalWorldSeg crossingPortal,
            int maxTransitions,
            out NavLocation nextLoc,
            out int exitXcm,
            out int exitZcm,
            out int nextXcm,
            out int nextZcm,
            out Fix64 stepCost)
        {
            nextLoc = default;
            exitXcm = 0;
            exitZcm = 0;
            nextXcm = 0;
            nextZcm = 0;
            stepCost = Fix64.Zero;

            const int denominator = 32;
            for (int i = 0; i <= denominator; i++)
            {
                int numerator = ResolvePortalSampleNumerator(i, denominator);
                int sampleXcm = crossingPortal.Ax + ((crossingPortal.Bx - crossingPortal.Ax) * numerator / denominator);
                int sampleZcm = crossingPortal.Az + ((crossingPortal.Bz - crossingPortal.Az) * numerator / denominator);
                if (!TryProjectPortalSampleIntoTile(
                        currentTile,
                        sampleXcm,
                        sampleZcm,
                        out _,
                        out int candidateExitXcm,
                        out int candidateExitZcm) ||
                    !TryCanReachPointInTile(
                        currentTile,
                        startTri,
                        startXcm,
                        startZcm,
                        candidateExitXcm,
                        candidateExitZcm,
                        maxTransitions,
                        out _,
                        out Fix64 candidateStepCost) ||
                    !TryProjectPortalSampleIntoTile(
                        neighborTile,
                        sampleXcm,
                        sampleZcm,
                        out NavLocation candidateNextLoc,
                        out int candidateNextXcm,
                        out int candidateNextZcm))
                {
                    continue;
                }

                nextLoc = candidateNextLoc;
                exitXcm = candidateExitXcm;
                exitZcm = candidateExitZcm;
                nextXcm = candidateNextXcm;
                nextZcm = candidateNextZcm;
                stepCost = candidateStepCost +
                    (Dist(exitXcm, exitZcm, nextXcm, nextZcm) * PortalCost(neighborTile, neighborPortalIndex));
                return true;
            }

            return false;
        }

        private static int ResolvePortalSampleNumerator(int index, int denominator)
        {
            if (index <= 0)
            {
                return denominator / 2;
            }

            int offset = (index + 1) / 2;
            int numerator = (index & 1) == 1
                ? (denominator / 2) - offset
                : (denominator / 2) + offset;
            return Math.Clamp(numerator, 0, denominator);
        }

        private bool TryCanReachPointInTile(
            NavTile tile,
            int startTri,
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            int maxTransitions,
            out int goalTri,
            out Fix64 travelCost)
        {
            goalTri = -1;
            travelCost = Fix64.Zero;
            if ((uint)startTri >= (uint)tile.TriangleCount ||
                !TryProjectIntoTile(tile, goalXcm, goalZcm, out NavLocation goalLoc))
            {
                return false;
            }

            goalTri = goalLoc.TriangleId;
            int startLocalX = WorldToTileLocalX(tile, startXcm);
            int startLocalZ = WorldToTileLocalZ(tile, startZcm);
            if (startTri == goalTri ||
                IsSegmentInsideTileNavMesh(tile, startLocalX, startLocalZ, goalLoc.LocalXcm, goalLoc.LocalZcm))
            {
                travelCost = Dist(startXcm, startZcm, goalXcm, goalZcm) * GetTriangleCost(tile, startTri);
                return true;
            }

            if (!AreTrianglesConnected(tile, startTri, goalTri))
            {
                return false;
            }

            travelCost = Dist(startXcm, startZcm, goalXcm, goalZcm) * GetTriangleCost(tile, startTri);
            return true;
        }

        private bool TryAppendTileSegment(
            NavTile tile,
            int startTri,
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            int maxPoints,
            List<int> xs,
            List<int> zs,
            ref Fix64 travelCost,
            out int goalTri)
        {
            goalTri = -1;
            if ((uint)startTri >= (uint)tile.TriangleCount ||
                !TryProjectIntoTile(tile, goalXcm, goalZcm, out NavLocation goalLoc))
            {
                return false;
            }

            goalTri = goalLoc.TriangleId;
            int startLocalX = WorldToTileLocalX(tile, startXcm);
            int startLocalZ = WorldToTileLocalZ(tile, startZcm);
            if (startTri == goalTri ||
                IsSegmentInsideTileNavMesh(tile, startLocalX, startLocalZ, goalLoc.LocalXcm, goalLoc.LocalZcm))
            {
                travelCost += Dist(startXcm, startZcm, goalXcm, goalZcm) * GetTriangleCost(tile, startTri);
                return TryAppendPoint(xs, zs, goalXcm, goalZcm, maxPoints);
            }

            var startLoc = new NavLocation(tile.TileId, tile.TileVersion, startTri, startLocalX, startLocalZ);
            int remainingTransitions = Math.Max(1, maxPoints - xs.Count);
            if (!TryBuildSameTileTrianglePath(
                    tile,
                    startLoc,
                    goalLoc,
                    startXcm,
                    startZcm,
                    goalXcm,
                    goalZcm,
                    remainingTransitions,
                    out int[] segmentX,
                    out int[] segmentZ,
                    out Fix64 segmentCost))
            {
                return false;
            }

            for (int i = 1; i < segmentX.Length; i++)
            {
                if (!TryAppendPoint(xs, zs, segmentX[i], segmentZ[i], maxPoints))
                {
                    return false;
                }
            }

            travelCost += segmentCost;
            return true;
        }

        private bool TryProjectIntoTile(NavTile tile, int worldXcm, int worldZcm, out NavLocation loc)
        {
            loc = default;
            int localXcm = WorldToTileLocalX(tile, worldXcm);
            int localZcm = WorldToTileLocalZ(tile, worldZcm);
            int triId = FindContainingTriangle(tile, localXcm, localZcm);
            if (triId < 0)
            {
                triId = FindNearestTriangle(tile, localXcm, localZcm, maxSurfaceDistanceCm: 100);
            }

            if (triId < 0)
            {
                return false;
            }

            loc = new NavLocation(tile.TileId, tile.TileVersion, triId, localXcm, localZcm);
            return true;
        }

        private bool TryProjectPortalIntoTile(
            NavTile tile,
            PortalWorldSeg portal,
            out NavLocation loc,
            out int worldXcm,
            out int worldZcm)
        {
            worldXcm = (portal.Ax + portal.Bx) / 2;
            worldZcm = (portal.Az + portal.Bz) / 2;
            if (TryProjectPortalSampleIntoTile(tile, worldXcm, worldZcm, out loc, out worldXcm, out worldZcm))
            {
                return true;
            }

            // Portal endpoints may round just outside one side of a scaled runtime tile.
            // Try a few deterministic interior samples before declaring the transition closed.
            for (int numerator = 1; numerator <= 3; numerator++)
            {
                worldXcm = portal.Ax + ((portal.Bx - portal.Ax) * numerator / 4);
                worldZcm = portal.Az + ((portal.Bz - portal.Az) * numerator / 4);
                if (TryProjectPortalSampleIntoTile(tile, worldXcm, worldZcm, out loc, out worldXcm, out worldZcm))
                {
                    return true;
                }
            }

            loc = default;
            return false;
        }

        private bool TryProjectPortalSampleIntoTile(
            NavTile tile,
            int sampleWorldXcm,
            int sampleWorldZcm,
            out NavLocation loc,
            out int projectedWorldXcm,
            out int projectedWorldZcm)
        {
            loc = default;
            projectedWorldXcm = sampleWorldXcm;
            projectedWorldZcm = sampleWorldZcm;

            int localXcm = WorldToTileLocalX(tile, sampleWorldXcm);
            int localZcm = WorldToTileLocalZ(tile, sampleWorldZcm);
            int triId = FindContainingTriangle(tile, localXcm, localZcm);
            if (triId >= 0)
            {
                loc = new NavLocation(tile.TileId, tile.TileVersion, triId, localXcm, localZcm);
                return true;
            }

            if (!TryFindNearestTriangleProjection(
                    tile,
                    localXcm,
                    localZcm,
                    ResolvePortalProjectionDistanceCm(),
                    out triId,
                    out int projectedLocalXcm,
                    out int projectedLocalZcm))
            {
                return false;
            }

            projectedWorldXcm = TileLocalToWorldX(tile, projectedLocalXcm);
            projectedWorldZcm = TileLocalToWorldZ(tile, projectedLocalZcm);
            loc = new NavLocation(tile.TileId, tile.TileVersion, triId, projectedLocalXcm, projectedLocalZcm);
            return true;
        }

        private int ResolvePortalProjectionDistanceCm()
        {
            int minExtent = Math.Min(_bakedTileWidthCm, _bakedTileHeightCm);
            if (minExtent <= 0)
            {
                return 512;
            }

            return Math.Clamp(minExtent / 3, 512, 4096);
        }

        private int ResolvePointProjectionDistanceCm()
        {
            int minExtent = Math.Min(_bakedTileWidthCm, _bakedTileHeightCm);
            if (minExtent <= 0)
            {
                return 50;
            }

            return Math.Clamp(minExtent / 64, 50, 512);
        }

        private static bool TryFindNearestTriangleProjection(
            NavTile tile,
            int localXcm,
            int localZcm,
            int maxSurfaceDistanceCm,
            out int triangleId,
            out int projectedLocalXcm,
            out int projectedLocalZcm)
        {
            triangleId = -1;
            projectedLocalXcm = 0;
            projectedLocalZcm = 0;
            long maxD2 = (long)Math.Max(0, maxSurfaceDistanceCm) * Math.Max(0, maxSurfaceDistanceCm);
            long bestD2 = long.MaxValue;

            for (int i = 0; i < tile.TriangleCount; i++)
            {
                int a = tile.TriA[i];
                int b = tile.TriB[i];
                int c = tile.TriC[i];
                FindClosestPointOnTriangle(
                    localXcm,
                    localZcm,
                    tile.VertexXcm[a],
                    tile.VertexZcm[a],
                    tile.VertexXcm[b],
                    tile.VertexZcm[b],
                    tile.VertexXcm[c],
                    tile.VertexZcm[c],
                    out int candidateX,
                    out int candidateZ,
                    out long candidateD2);

                if (candidateD2 >= bestD2)
                {
                    continue;
                }

                bestD2 = candidateD2;
                triangleId = i;
                projectedLocalXcm = candidateX;
                projectedLocalZcm = candidateZ;
            }

            return triangleId >= 0 && bestD2 <= maxD2;
        }

        private static void FindClosestPointOnTriangle(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz,
            out int closestX,
            out int closestZ,
            out long distanceSquared)
        {
            if (PointInTriangle(px, pz, ax, az, bx, bz, cx, cz))
            {
                closestX = px;
                closestZ = pz;
                distanceSquared = 0;
                return;
            }

            ClosestPointOnSegment(px, pz, ax, az, bx, bz, out int abX, out int abZ, out long abD2);
            ClosestPointOnSegment(px, pz, bx, bz, cx, cz, out int bcX, out int bcZ, out long bcD2);
            ClosestPointOnSegment(px, pz, cx, cz, ax, az, out int caX, out int caZ, out long caD2);

            closestX = abX;
            closestZ = abZ;
            distanceSquared = abD2;
            if (bcD2 < distanceSquared)
            {
                closestX = bcX;
                closestZ = bcZ;
                distanceSquared = bcD2;
            }

            if (caD2 < distanceSquared)
            {
                closestX = caX;
                closestZ = caZ;
                distanceSquared = caD2;
            }
        }

        private static void ClosestPointOnSegment(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            out int closestX,
            out int closestZ,
            out long distanceSquared)
        {
            long dx = (long)bx - ax;
            long dz = (long)bz - az;
            long len2 = (dx * dx) + (dz * dz);
            if (len2 <= 0)
            {
                closestX = ax;
                closestZ = az;
                distanceSquared = DistanceSquared(px, pz, ax, az);
                return;
            }

            long dot = (((long)px - ax) * dx) + (((long)pz - az) * dz);
            if (dot <= 0)
            {
                closestX = ax;
                closestZ = az;
                distanceSquared = DistanceSquared(px, pz, ax, az);
                return;
            }

            if (dot >= len2)
            {
                closestX = bx;
                closestZ = bz;
                distanceSquared = DistanceSquared(px, pz, bx, bz);
                return;
            }

            closestX = ax + (int)DivRound(dx * dot, len2);
            closestZ = az + (int)DivRound(dz * dot, len2);
            distanceSquared = DistanceSquared(px, pz, closestX, closestZ);
        }

        private static long DivRound(long numerator, long denominator)
        {
            if (denominator <= 0)
            {
                return 0;
            }

            return numerator >= 0
                ? (numerator + (denominator / 2)) / denominator
                : (numerator - (denominator / 2)) / denominator;
        }

        private static bool TryAppendPoint(List<int> xs, List<int> zs, int xcm, int zcm, int maxPoints)
        {
            if (xs.Count > 0 && xs[^1] == xcm && zs[^1] == zcm)
            {
                return true;
            }

            if (xs.Count >= maxPoints)
            {
                return false;
            }

            xs.Add(xcm);
            zs.Add(zcm);
            return true;
        }

        private void SimplifySameTilePath(
            NavTile tile,
            int[] sourceXcm,
            int[] sourceZcm,
            out int[] pathXcm,
            out int[] pathZcm)
        {
            if (sourceXcm.Length <= 2 || sourceXcm.Length != sourceZcm.Length)
            {
                pathXcm = sourceXcm;
                pathZcm = sourceZcm;
                return;
            }

            var xs = new List<int>(sourceXcm.Length) { sourceXcm[0] };
            var zs = new List<int>(sourceZcm.Length) { sourceZcm[0] };
            int anchor = 0;
            int last = sourceXcm.Length - 1;
            while (anchor < last)
            {
                int next = anchor + 1;
                int anchorLocalX = WorldToTileLocalX(tile, sourceXcm[anchor]);
                int anchorLocalZ = WorldToTileLocalZ(tile, sourceZcm[anchor]);
                for (int candidate = anchor + 2; candidate <= last; candidate++)
                {
                    int candidateLocalX = WorldToTileLocalX(tile, sourceXcm[candidate]);
                    int candidateLocalZ = WorldToTileLocalZ(tile, sourceZcm[candidate]);
                    if (!IsSegmentInsideTileNavMesh(tile, anchorLocalX, anchorLocalZ, candidateLocalX, candidateLocalZ))
                    {
                        break;
                    }

                    next = candidate;
                }

                if (next <= anchor)
                {
                    next = anchor + 1;
                }

                xs.Add(sourceXcm[next]);
                zs.Add(sourceZcm[next]);
                anchor = next;
            }

            pathXcm = xs.ToArray();
            pathZcm = zs.ToArray();
        }

        private static bool IsSegmentInsideTileNavMesh(
            NavTile tile,
            int startLocalXcm,
            int startLocalZcm,
            int goalLocalXcm,
            int goalLocalZcm)
        {
            long dx = (long)goalLocalXcm - startLocalXcm;
            long dz = (long)goalLocalZcm - startLocalZcm;
            long lenSq = (dx * dx) + (dz * dz);
            if (lenSq <= 0)
            {
                return FindContainingTriangle(tile, startLocalXcm, startLocalZcm) >= 0;
            }

            long len = DeterministicLongSqrt(lenSq);
            int sampleCount = Math.Clamp((int)((len + 249) / 250), 1, 256);
            for (int i = 1; i < sampleCount; i++)
            {
                long x = startLocalXcm + (dx * i / sampleCount);
                long z = startLocalZcm + (dz * i / sampleCount);
                int tri = FindContainingTriangle(tile, (int)x, (int)z);
                if (tri < 0)
                {
                    tri = FindNearestTriangle(tile, (int)x, (int)z, maxSurfaceDistanceCm: 20);
                }

                if (tri < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryResolveNeighborTileFromPortal(NavTile tile, PortalWorldSeg portal, out NavTileId neighborId)
        {
            int minX = _worldMinXcm + GetRuntimeTileOriginXcm(tile);
            int minZ = _worldMinZcm + GetRuntimeTileOriginZcm(tile);
            int maxX = minX + _tileWidthCm;
            int maxZ = minZ + _tileHeightCm;
            int toleranceX = Math.Max(100, _tileWidthCm / 200);
            int toleranceZ = Math.Max(100, _tileHeightCm / 200);

            int midX = (portal.Ax + portal.Bx) / 2;
            int midZ = (portal.Az + portal.Bz) / 2;
            if (Math.Abs(midX - minX) <= toleranceX)
            {
                neighborId = new NavTileId(tile.TileId.ChunkX - 1, tile.TileId.ChunkY, tile.TileId.Layer);
                return true;
            }

            if (Math.Abs(midX - maxX) <= toleranceX)
            {
                neighborId = new NavTileId(tile.TileId.ChunkX + 1, tile.TileId.ChunkY, tile.TileId.Layer);
                return true;
            }

            if (Math.Abs(midZ - minZ) <= toleranceZ)
            {
                neighborId = new NavTileId(tile.TileId.ChunkX, tile.TileId.ChunkY - 1, tile.TileId.Layer);
                return true;
            }

            if (Math.Abs(midZ - maxZ) <= toleranceZ)
            {
                neighborId = new NavTileId(tile.TileId.ChunkX, tile.TileId.ChunkY + 1, tile.TileId.Layer);
                return true;
            }

            neighborId = default;
            return false;
        }

        private static bool TryBuildFunnelPath(
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            IReadOnlyList<PortalWorldSeg> pathPortals,
            out int[] pathXcm,
            out int[] pathZcm)
        {
            pathXcm = Array.Empty<int>();
            pathZcm = Array.Empty<int>();
            if (pathPortals == null)
            {
                return false;
            }

            var portals = new List<(int LeftXcm, int LeftZcm, int RightXcm, int RightZcm)>(pathPortals.Count);
            for (int i = 0; i < pathPortals.Count; i++)
            {
                PortalWorldSeg seg = pathPortals[i];
                if (seg.Ax == seg.Bx && seg.Az == seg.Bz)
                {
                    return false;
                }

                portals.Add((seg.Ax, seg.Az, seg.Bx, seg.Bz));
            }

            FunnelResult result = FunnelAlgorithm.SmoothPathCm(startXcm, startZcm, goalXcm, goalZcm, portals);
            if (!result.Success || result.Path.Length < 2)
            {
                return false;
            }

            (pathXcm, pathZcm) = FunnelAlgorithm.ToIntPath(result);
            if (pathXcm.Length != pathZcm.Length || pathXcm.Length < 2)
            {
                pathXcm = Array.Empty<int>();
                pathZcm = Array.Empty<int>();
                return false;
            }

            pathXcm[0] = startXcm;
            pathZcm[0] = startZcm;
            pathXcm[^1] = goalXcm;
            pathZcm[^1] = goalZcm;
            return true;
        }
    }
}
