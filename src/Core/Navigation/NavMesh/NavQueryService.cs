using System;
using System.Collections.Generic;
using Ludots.Core.Collections;
using Ludots.Core.Map.Hex;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;

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

        public NavQueryService(NavTileStore store, int layer = 0, NavAreaCostTable areaCosts = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _layer = layer;
            _areaCosts = areaCosts ?? NavAreaCostTable.CreateDefault();
            _minCost = _areaCosts.MinCost;
        }

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

            int localXcm = worldXcm - tile.OriginXcm;
            int localZcm = worldZcm - tile.OriginZcm;
            int triId = FindNearestTriangle(tile, localXcm, localZcm);
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
                Fix64 cost = Dist(startXcm, startZcm, goalXcm, goalZcm) * _areaCosts.Get(tile.TriAreaIds[startLoc.TriangleId]);
                return new NavPathResult(NavPathStatus.Ok, new[] { startXcm, goalXcm }, new[] { startZcm, goalZcm }, cost);
            }

            var pathPortals = FindPortalPath(startLoc.TileId, startXcm, startZcm, goalLoc.TileId, goalXcm, goalZcm, maxPortals, out var travelCost);
            if (pathPortals == null) return new NavPathResult(NavPathStatus.NotReachable, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);

            var xs = new List<int>(pathPortals.Count + 2) { startXcm };
            var zs = new List<int>(pathPortals.Count + 2) { startZcm };
            for (int i = 0; i < pathPortals.Count; i++)
            {
                var p = pathPortals[i];
                int midX = (p.Ax + p.Bx) / 2;
                int midZ = (p.Az + p.Bz) / 2;
                xs.Add(midX);
                zs.Add(midZ);
            }
            xs.Add(goalXcm);
            zs.Add(goalZcm);

            return new NavPathResult(NavPathStatus.Ok, xs.ToArray(), zs.ToArray(), travelCost);
        }

        /// <summary>
        /// Tile width/height in centimeters as Fix64 for deterministic tile location.
        /// Computed once from HexCoordinates constants * ChunkSize * 100 (cm conversion).
        /// </summary>
        private static readonly Fix64 TileWidthCm =
            Fix64.FromFloat(HexCoordinates.HexWidth * VertexChunk.ChunkSize * 100f);
        private static readonly Fix64 TileHeightCm =
            Fix64.FromFloat(HexCoordinates.RowSpacing * VertexChunk.ChunkSize * 100f);

        private NavTileId LocateTile(int worldXcm, int worldZcm)
        {
            // Deterministic floor division using Fix64
            var xFix = Fix64.FromInt(worldXcm);
            var zFix = Fix64.FromInt(worldZcm);
            int cx = (xFix / TileWidthCm).ToInt();  // Fix64 division truncates towards zero
            int cz = (zFix / TileHeightCm).ToInt();
            // Correct for negative coordinates (floor division)
            if (xFix < Fix64.Zero && xFix % TileWidthCm != Fix64.Zero) cx--;
            if (zFix < Fix64.Zero && zFix % TileHeightCm != Fix64.Zero) cz--;
            if (cx < 0) cx = 0;
            if (cz < 0) cz = 0;
            return new NavTileId(cx, cz, _layer);
        }

        private static int FindNearestTriangle(NavTile tile, int localXcm, int localZcm)
        {
            int best = -1;
            long bestD2 = long.MaxValue;
            for (int i = 0; i < tile.TriangleCount; i++)
            {
                int a = tile.TriA[i];
                int b = tile.TriB[i];
                int c = tile.TriC[i];

                int cx = (tile.VertexXcm[a] + tile.VertexXcm[b] + tile.VertexXcm[c]) / 3;
                int cz = (tile.VertexZcm[a] + tile.VertexZcm[b] + tile.VertexZcm[c]) / 3;
                long dx = (long)cx - localXcm;
                long dz = (long)cz - localZcm;
                long d2 = dx * dx + dz * dz;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = i;
                }
            }
            return best;
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

        private readonly struct PortalKey : IEquatable<PortalKey>
        {
            // Tolerance in cm for matching portals (handles floating-point rounding)
            private const int Tolerance = 2;

            public readonly int Ax;
            public readonly int Az;
            public readonly int Bx;
            public readonly int Bz;

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

        private List<PortalWorldSeg> FindPortalPath(NavTileId startTileId, int startXcm, int startZcm, NavTileId goalTileId, int goalXcm, int goalZcm, int maxPortals, out Fix64 travelCost)
        {
            travelCost = Fix64.Zero;
            var startTile = _store.GetOrLoad(startTileId);
            var goalTile = _store.GetOrLoad(goalTileId);

            var nodeIndex = new Dictionary<PortalRef, int>(128);
            var nodes = new List<Node>(128);
            var open = new Fix64PriorityQueue<int>(128);
            var closed = new HashSet<int>();

            int startNode = AddStartVirtualNode(nodes);
            int goalNode = AddGoalVirtualNode(nodes);

            void EnqueueStartNeighbors()
            {
                for (int i = 0; i < startTile.Portals.Length; i++)
                {
                    var pref = new PortalRef(startTileId, i);
                    int nid = GetOrCreateNode(pref, startTile, nodes, nodeIndex);
                    Fix64 g = Dist(startXcm, startZcm, GetPortalMidWorldXcm(startTile, i), GetPortalMidWorldZcm(startTile, i)) * PortalCost(startTile, i);
                    nodes[nid].G = g;
                    nodes[nid].F = g + Dist(GetPortalMidWorldXcm(startTile, i), GetPortalMidWorldZcm(startTile, i), goalXcm, goalZcm) * _minCost;
                    nodes[nid].Prev = startNode;
                    open.Enqueue(nid, nodes[nid].F);
                }
            }

            EnqueueStartNeighbors();

            var portalKeyCache = new Dictionary<NavTileId, Dictionary<PortalKey, int>>();
            Dictionary<PortalKey, int> GetPortalMap(NavTile tile)
            {
                if (portalKeyCache.TryGetValue(tile.TileId, out var cached)) return cached;
                var dict = new Dictionary<PortalKey, int>(tile.Portals.Length);
                for (int i = 0; i < tile.Portals.Length; i++)
                {
                    var seg = GetPortalWorldSeg(tile, i);
                    dict[new PortalKey(seg.Ax, seg.Az, seg.Bx, seg.Bz)] = i;
                }
                portalKeyCache[tile.TileId] = dict;
                return dict;
            }

            while (open.TryDequeue(out int current, out _))
            {
                if (!closed.Add(current)) continue;
                var curRef = nodes[current].Ref;
                if (curRef.TileId.Equals(goalTileId))
                {
                    Fix64 endG = nodes[current].G + Dist(GetPortalMidWorldXcm(goalTile, curRef.PortalIndex), GetPortalMidWorldZcm(goalTile, curRef.PortalIndex), goalXcm, goalZcm) * PortalCost(goalTile, curRef.PortalIndex);
                    nodes[goalNode].G = endG;
                    nodes[goalNode].Prev = current;
                    travelCost = endG;
                    return Reconstruct(nodes, goalNode, startNode, maxPortals);
                }

                var curTile = _store.GetOrLoad(curRef.TileId);
                int curMidX = GetPortalMidWorldXcm(curTile, curRef.PortalIndex);
                int curMidZ = GetPortalMidWorldZcm(curTile, curRef.PortalIndex);

                for (int i = 0; i < curTile.Portals.Length; i++)
                {
                    if (i == curRef.PortalIndex) continue;
                    var nextRef = new PortalRef(curRef.TileId, i);
                    int nextId = GetOrCreateNode(nextRef, curTile, nodes, nodeIndex);
                    Fix64 g = nodes[current].G + Dist(curMidX, curMidZ, GetPortalMidWorldXcm(curTile, i), GetPortalMidWorldZcm(curTile, i)) * PortalCost(curTile, i);
                    if (closed.Contains(nextId) && g >= nodes[nextId].G) continue;
                    if (nodes[nextId].Prev < 0 || g < nodes[nextId].G)
                    {
                        // Re-opening a closed node: remove from closed so it will be
                        // re-expanded when dequeued, propagating the shorter path.
                        if (closed.Contains(nextId))
                        {
                            closed.Remove(nextId);
                        }
                        nodes[nextId].G = g;
                        nodes[nextId].F = g + Dist(GetPortalMidWorldXcm(curTile, i), GetPortalMidWorldZcm(curTile, i), goalXcm, goalZcm) * _minCost;
                        nodes[nextId].Prev = current;
                        open.Enqueue(nextId, nodes[nextId].F);
                    }
                }

                var segCur = GetPortalWorldSeg(curTile, curRef.PortalIndex);
                var key = new PortalKey(segCur.Ax, segCur.Az, segCur.Bx, segCur.Bz);
                var neighborTileId = GetNeighborTileId(curTile.TileId, curTile.Portals[curRef.PortalIndex].Side);
                if (neighborTileId.ChunkX >= 0 && neighborTileId.ChunkY >= 0)
                {
                    NavTile neighborTile;
                    try
                    {
                        neighborTile = _store.GetOrLoad(neighborTileId);
                    }
                    catch
                    {
                        neighborTile = null;
                    }

                    if (neighborTile != null)
                    {
                        var dict = GetPortalMap(neighborTile);
                        if (dict.TryGetValue(key, out int neighborPortalIndex))
                        {
                            var nextRef = new PortalRef(neighborTileId, neighborPortalIndex);
                            int nextId = GetOrCreateNode(nextRef, neighborTile, nodes, nodeIndex);
                            Fix64 g = nodes[current].G + Dist(curMidX, curMidZ, GetPortalMidWorldXcm(neighborTile, neighborPortalIndex), GetPortalMidWorldZcm(neighborTile, neighborPortalIndex)) * PortalCost(neighborTile, neighborPortalIndex);
                            if (closed.Contains(nextId) && g >= nodes[nextId].G) continue;
                            if (nodes[nextId].Prev < 0 || g < nodes[nextId].G)
                            {
                                if (closed.Contains(nextId))
                                {
                                    closed.Remove(nextId);
                                }
                                nodes[nextId].G = g;
                                nodes[nextId].F = g + Dist(GetPortalMidWorldXcm(neighborTile, neighborPortalIndex), GetPortalMidWorldZcm(neighborTile, neighborPortalIndex), goalXcm, goalZcm) * _minCost;
                                nodes[nextId].Prev = current;
                                open.Enqueue(nextId, nodes[nextId].F);
                            }
                        }
                    }
                }
            }

            travelCost = Fix64.Zero;
            return null;
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

        private static int GetOrCreateNode(PortalRef pref, NavTile tile, List<Node> nodes, Dictionary<PortalRef, int> nodeIndex)
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

        private static PortalWorldSeg GetPortalWorldSeg(NavTile tile, int portalIndex)
        {
            var p = tile.Portals[portalIndex];
            int ax = tile.OriginXcm + p.LeftXcm;
            int az = tile.OriginZcm + p.LeftZcm;
            int bx = tile.OriginXcm + p.RightXcm;
            int bz = tile.OriginZcm + p.RightZcm;
            return new PortalWorldSeg(ax, az, bx, bz);
        }

        private static int GetPortalMidWorldXcm(NavTile tile, int portalIndex)
        {
            var seg = GetPortalWorldSeg(tile, portalIndex);
            return (seg.Ax + seg.Bx) / 2;
        }

        private static int GetPortalMidWorldZcm(NavTile tile, int portalIndex)
        {
            var seg = GetPortalWorldSeg(tile, portalIndex);
            return (seg.Az + seg.Bz) / 2;
        }

        private Fix64 PortalCost(NavTile tile, int portalIndex)
        {
            if (tile.TriangleCount == 0) return Fix64.OneValue;
            int wx = GetPortalMidWorldXcm(tile, portalIndex);
            int wz = GetPortalMidWorldZcm(tile, portalIndex);
            int localX = wx - tile.OriginXcm;
            int localZ = wz - tile.OriginZcm;
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
    }
}
