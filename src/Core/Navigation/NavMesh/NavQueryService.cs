using System;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation.NavMesh
{
    public enum NavPathStatus : byte
    {
        Ok = 0,
        NotReady = 1,
        NotReachable = 2,
        InvalidInput = 3,

        /// <summary>
        /// A walkable prefix of the route was solved, but the requested goal is not
        /// reachable from the start. The path is the furthest reachable segment; its
        /// endpoint is the nearest legal stand point toward the goal.
        /// </summary>
        Partial = 4
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

        /// <summary>
        /// World-space end point of the returned path. For <see cref="NavPathStatus.Ok"/> this is
        /// the requested goal; for <see cref="NavPathStatus.Partial"/> it is the nearest legal
        /// stand point the query could reach toward the goal. Undefined when no path was produced.
        /// </summary>
        public readonly int ResolvedGoalXcm;
        public readonly int ResolvedGoalZcm;

        public NavPathResult(NavPathStatus status, int[] pathXcm, int[] pathZcm, Fix64 travelCost)
            : this(status, pathXcm, pathZcm, travelCost, 0, 0)
        {
        }

        public NavPathResult(
            NavPathStatus status,
            int[] pathXcm,
            int[] pathZcm,
            Fix64 travelCost,
            int resolvedGoalXcm,
            int resolvedGoalZcm)
        {
            Status = status;
            PathXcm = pathXcm ?? Array.Empty<int>();
            PathZcm = pathZcm ?? Array.Empty<int>();
            TravelCost = travelCost;
            ResolvedGoalXcm = resolvedGoalXcm;
            ResolvedGoalZcm = resolvedGoalZcm;
        }
    }

    public sealed class NavQueryService
    {
        private const int MaxStableRevisionAttempts = 2;

        private readonly NavTileStore _store;
        private readonly int _layer;
        private readonly NavAreaCostTable _areaCosts;
        private readonly Fix64 _tileWidthCm;
        private readonly Fix64 _tileHeightCm;
        private readonly int _originXcm;
        private readonly int _originZcm;

        public NavQueryService(
            NavTileStore store,
            int layer,
            NavAreaCostTable areaCosts,
            int tileWidthCm,
            int tileHeightCm,
            int originXcm = 0,
            int originZcm = 0)
            : this(
                store,
                layer,
                areaCosts,
                Fix64.FromInt(RequirePositive(tileWidthCm, nameof(tileWidthCm))),
                Fix64.FromInt(RequirePositive(tileHeightCm, nameof(tileHeightCm))),
                originXcm,
                originZcm)
        {
        }

        private NavQueryService(
            NavTileStore store,
            int layer,
            NavAreaCostTable areaCosts,
            Fix64 tileWidthCm,
            Fix64 tileHeightCm,
            int originXcm,
            int originZcm)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _layer = layer;
            _areaCosts = areaCosts ?? NavAreaCostTable.CreateDefault();
            _tileWidthCm = tileWidthCm;
            _tileHeightCm = tileHeightCm;
            _originXcm = originXcm;
            _originZcm = originZcm;
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
            int localZcm = worldZcm - tile.OriginYcm;
            int triId = FindNearestTriangle(tile, localXcm, localZcm);
            if (triId < 0) return false;

            loc = new NavLocation(tile.TileId, tile.TileVersion, triId, localXcm, localZcm);
            return true;
        }

        /// <summary>
        /// Snaps a world point back onto the walkable navigation surface. A point already inside a
        /// walkable polygon is returned unchanged; a point outside every walkable polygon (the far
        /// side of a gap, a slope too steep to climb, or anywhere an agent's local steering drifted
        /// to) is pulled to the closest point on the nearest walkable polygon boundary.
        /// <para>
        /// This is the corridor correction primitive: callers constrain a desired position to the
        /// navigation surface instead of validating afterwards. It returns false only when no
        /// walkable surface exists at all near the point, in which case <paramref name="outXcm"/> /
        /// <paramref name="outZcm"/> are left untouched.
        /// </para>
        /// </summary>
        public bool TrySnapToSurface(int worldXcm, int worldZcm, out int outXcm, out int outZcm)
        {
            outXcm = worldXcm;
            outZcm = worldZcm;

            if (!TryProject(worldXcm, worldZcm, out NavLocation loc))
            {
                return false;
            }

            NavTile tile;
            try
            {
                tile = _store.GetOrLoad(loc.TileId);
            }
            catch
            {
                return false;
            }

            if (_store.SnapshotLoadedTiles() is not { Length: > 0 } tiles)
            {
                return false;
            }

            var navMesh = DetourNavQueryEngine.BuildNavMeshForSurfaceQueries(
                tiles,
                _layer,
                _tileWidthCm.RoundToInt(),
                _tileHeightCm.RoundToInt());
            if (navMesh == null)
            {
                return false;
            }

            var query = new DotRecast.Detour.DtNavMeshQuery(navMesh);
            var filter = DetourNavQueryEngine.BuildDefaultFilter(_areaCosts);

            float tileWidthM = _tileWidthCm.RoundToInt() / 100f;
            float tileHeightM = _tileHeightCm.RoundToInt() / 100f;
            var extents = new DotRecast.Core.Numerics.RcVec3f(
                MathF.Max(1f, tileWidthM * 0.5f),
                256f,
                MathF.Max(1f, tileHeightM * 0.5f));

            var probe = new DotRecast.Core.Numerics.RcVec3f(worldXcm / 100f, 0f, worldZcm / 100f);
            var status = query.FindNearestPoly(probe, extents, filter, out long polyRef, out _, out _);
            if (status.Failed() || polyRef == 0)
            {
                return false;
            }

            // ClosestPointOnPoly (not ...Boundary): the boundary variant always projects onto the
            // polygon edge, which would drag every agent to a triangle edge and break formation
            // spacing. This variant returns the point unchanged when it already lies inside the
            // polygon, and only pulls it to the closest point when it is genuinely outside.
            status = query.ClosestPointOnPoly(polyRef, probe, out var closest, out _);
            if (status.Failed())
            {
                return false;
            }

            outXcm = (int)MathF.Round(closest.X * 100f);
            outZcm = (int)MathF.Round(closest.Z * 100f);
            return true;
        }

        public NavPathResult TryFindPath(int startXcm, int startZcm, int goalXcm, int goalZcm, int maxPortals = 256)
        {
            if (_store.TryRunStableRead(
                    () => TryFindPathCore(startXcm, startZcm, goalXcm, goalZcm, maxPortals),
                    out NavPathResult result,
                    MaxStableRevisionAttempts))
            {
                return result;
            }

            return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
        }

        private NavPathResult TryFindPathCore(int startXcm, int startZcm, int goalXcm, int goalZcm, int maxPortals)
        {
            try
            {
                _store.GetOrLoad(LocateTile(startXcm, startZcm));
                _store.GetOrLoad(LocateTile(goalXcm, goalZcm));

                return DetourNavQueryEngine.FindPath(
                    _store.SnapshotLoadedTiles(),
                    _layer,
                    _areaCosts,
                    _tileWidthCm.RoundToInt(),
                    _tileHeightCm.RoundToInt(),
                    startXcm,
                    startZcm,
                    goalXcm,
                    goalZcm,
                    maxPortals);
            }
            catch (InvalidOperationException)
            {
                return new NavPathResult(NavPathStatus.InvalidInput, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }
            catch
            {
                return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }
        }

        private NavTileId LocateTile(int worldXcm, int worldZcm)
        {
            int cx = FloorDiv(worldXcm - _originXcm, _tileWidthCm.RoundToInt());
            int cz = FloorDiv(worldZcm - _originZcm, _tileHeightCm.RoundToInt());
            return new NavTileId(cx, cz, _layer);
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private static int RequirePositive(int value, string name)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(name);
            return value;
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

                if (PointInTriangle2D(
                    localXcm,
                    localZcm,
                    tile.VertexXcm[a],
                    tile.VertexZcm[a],
                    tile.VertexXcm[b],
                    tile.VertexZcm[b],
                    tile.VertexXcm[c],
                    tile.VertexZcm[c]))
                {
                    return i;
                }

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

        private static bool PointInTriangle2D(
            double px,
            double pz,
            double ax,
            double az,
            double bx,
            double bz,
            double cx,
            double cz)
        {
            double v0x = cx - ax;
            double v0z = cz - az;
            double v1x = bx - ax;
            double v1z = bz - az;
            double v2x = px - ax;
            double v2z = pz - az;

            double dot00 = v0x * v0x + v0z * v0z;
            double dot01 = v0x * v1x + v0z * v1z;
            double dot02 = v0x * v2x + v0z * v2z;
            double dot11 = v1x * v1x + v1z * v1z;
            double dot12 = v1x * v2x + v1z * v2z;
            double denom = dot00 * dot11 - dot01 * dot01;
            if (Math.Abs(denom) <= 0.000001d) return false;

            double invDenom = 1d / denom;
            double u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            double v = (dot00 * dot12 - dot01 * dot02) * invDenom;
            const double Epsilon = 0.001d;
            return u >= -Epsilon && v >= -Epsilon && u + v <= 1d + Epsilon;
        }
    }
}
