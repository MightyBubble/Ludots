using System;
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
        private const int MaxStableRevisionAttempts = 2;

        private readonly NavTileStore _store;
        private readonly int _layer;
        private readonly NavAreaCostTable _areaCosts;
        private readonly Fix64 _tileWidthCm;
        private readonly Fix64 _tileHeightCm;
        private readonly int _originXcm;
        private readonly int _originZcm;
        private readonly int _widthChunks;
        private readonly int _heightChunks;

        public NavQueryService(NavTileStore store, int layer, NavAreaCostTable areaCosts, int tileWidthCm, int tileHeightCm)
            : this(store, layer, areaCosts, tileWidthCm, tileHeightCm, 0, 0)
        {
        }

        public NavQueryService(
            NavTileStore store,
            int layer,
            NavAreaCostTable areaCosts,
            int tileWidthCm,
            int tileHeightCm,
            int originXcm,
            int originZcm)
            : this(
                store,
                layer,
                areaCosts,
                Fix64.FromInt(RequirePositive(tileWidthCm, nameof(tileWidthCm))),
                Fix64.FromInt(RequirePositive(tileHeightCm, nameof(tileHeightCm))),
                originXcm,
                originZcm,
                0,
                0)
        {
        }

        public NavQueryService(
            NavTileStore store,
            int layer,
            NavAreaCostTable areaCosts,
            NavBoardTileGeometry geometry)
            : this(
                store,
                layer,
                areaCosts,
                Fix64.FromInt(geometry.TileWidthCm),
                Fix64.FromInt(geometry.TileHeightCm),
                geometry.OriginXcm,
                geometry.OriginZcm,
                geometry.WidthChunks,
                geometry.HeightChunks)
        {
        }

        private NavQueryService(
            NavTileStore store,
            int layer,
            NavAreaCostTable areaCosts,
            Fix64 tileWidthCm,
            Fix64 tileHeightCm,
            int originXcm,
            int originZcm,
            int widthChunks,
            int heightChunks)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _layer = layer;
            _areaCosts = areaCosts ?? NavAreaCostTable.CreateDefault();
            _tileWidthCm = tileWidthCm;
            _tileHeightCm = tileHeightCm;
            _originXcm = originXcm;
            _originZcm = originZcm;
            _widthChunks = widthChunks;
            _heightChunks = heightChunks;
        }

        public int OriginXcm => _originXcm;

        public int OriginZcm => _originZcm;

        private bool HasDeclaredExtent => _widthChunks > 0 && _heightChunks > 0;

        public bool TryProject(int worldXcm, int worldZcm, out NavLocation loc)
        {
            loc = default;
            var tileId = LocateTile(worldXcm, worldZcm);
            if (!IsInsideBoardExtent(tileId)) return false;
            NavTile tile;
            try
            {
                tile = _store.GetOrLoad(tileId);
            }
            catch
            {
                return false;
            }

            int boardLocalXcm = worldXcm - _originXcm - tile.OriginXcm;
            int boardLocalZcm = worldZcm - _originZcm - tile.OriginZcm;
            int triId = FindNearestTriangle(tile, boardLocalXcm, boardLocalZcm);
            if (triId < 0) return false;

            loc = new NavLocation(tile.TileId, tile.TileVersion, triId, boardLocalXcm, boardLocalZcm);
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
                NavTileId startTile = LocateTile(startXcm, startZcm);
                NavTileId goalTile = LocateTile(goalXcm, goalZcm);
                if (!IsInsideBoardExtent(startTile) || !IsInsideBoardExtent(goalTile))
                {
                    return new NavPathResult(NavPathStatus.InvalidInput, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
                }

                _store.GetOrLoad(startTile);
                _store.GetOrLoad(goalTile);

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
            // Board-local, then floor-divide into tile coordinates. Fix64.ToInt() is an
            // arithmetic right shift, so it already floors towards negative infinity; no
            // extra truncation correction is allowed here.
            var xFix = Fix64.FromInt(worldXcm - _originXcm);
            var zFix = Fix64.FromInt(worldZcm - _originZcm);
            return new NavTileId(
                (xFix / _tileWidthCm).ToInt(),
                (zFix / _tileHeightCm).ToInt(),
                _layer);
        }

        private bool IsInsideBoardExtent(NavTileId id)
        {
            if (!HasDeclaredExtent) return true;
            return id.ChunkX >= 0 && id.ChunkX < _widthChunks && id.ChunkY >= 0 && id.ChunkY < _heightChunks;
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
