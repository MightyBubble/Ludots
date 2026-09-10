using System;
using System.Collections.Generic;
using System.IO;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Navigation.NavMesh
{
    public static class DetourNavQueryEngine
    {
        private const int Nvp = DtDetour.DT_VERTS_PER_POLYGON;
        private const int WalkFlag = 1;
        private const int BorderLink = 0x8000;
        private const int BorderNoPortal = 0x8000 | 0xf;
        private const int MeshNullIndex = 0xffff;
        private const float QuantizationCellM = 0.01f;
        private const float QuantizationHeightM = 0.01f;
        private const int BorderToleranceCm = 2;

        /// <summary>
        /// 从已加载 NavTile 集合装配查询用 Detour 网格（每 tile 建 DtMeshData 含 BVH，代价高——
        /// 调用方应经 DetourQueryMeshCache 按 LoadedVersion 复用，禁止每查询重建）。
        /// </summary>
        public static DtNavMesh? BuildDetourNavMesh(NavTile[] tiles, int layer, int tileWidthCm, int tileHeightCm)
        {
            if (tiles == null || tiles.Length == 0)
            {
                return null;
            }

            return BuildNavMesh(tiles, layer, tileWidthCm, tileHeightCm);
        }

        public static NavPathResult FindPath(
            DtNavMesh navMesh,
            NavAreaCostTable areaCosts,
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            int maxPortals)
        {
            if (navMesh == null)
            {
                return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }

            return FindPathCore(navMesh, areaCosts, startXcm, startZcm, goalXcm, goalZcm, maxPortals);
        }

        public static NavPathResult FindPathFromDetourTileBytes(
            IReadOnlyList<byte[]> detourTilePayloads,
            int layer,
            NavAreaCostTable areaCosts,
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            int maxPortals)
        {
            if (detourTilePayloads == null || detourTilePayloads.Count == 0)
            {
                return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }

            var meshData = new List<DtMeshData>(detourTilePayloads.Count);
            var reader = new DtMeshDataReader();
            for (int i = 0; i < detourTilePayloads.Count; i++)
            {
                byte[] payload = detourTilePayloads[i];
                if (payload == null || payload.Length == 0)
                {
                    continue;
                }

                using var ms = new MemoryStream(payload);
                using var br = new BinaryReader(ms);
                DtMeshData data = reader.Read(br, DtDetour.DT_VERTS_PER_POLYGON);
                if (data?.header == null || data.header.layer != layer || data.header.polyCount <= 0)
                {
                    continue;
                }

                meshData.Add(data);
            }

            if (meshData.Count == 0)
            {
                return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }

            var navMesh = BuildNavMeshFromDetourTiles(meshData);
            if (navMesh == null)
            {
                return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }

            return FindPath(navMesh, areaCosts, startXcm, startZcm, goalXcm, goalZcm, maxPortals);
        }

        public static byte[] BuildDetourTileBytes(NavTile tile, int tileWidthCm, int tileHeightCm)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            if (tileWidthCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidthCm), "Tile width must be positive.");
            if (tileHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeightCm), "Tile height must be positive.");

            DtMeshData data = BuildTileData(tile, tileWidthCm, tileHeightCm)
                ?? throw new InvalidOperationException($"Failed to build Detour tile data for NavTile {tile.TileId}.");

            return WriteDetourTileBytes(data);
        }

        public static byte[] BuildFlatGridBaselineDetourTileBytes(NavTile tile, int tileWidthCm, int tileHeightCm)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            if (tileWidthCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidthCm), "Tile width must be positive.");
            if (tileHeightCm <= 0) throw new ArgumentOutOfRangeException(nameof(tileHeightCm), "Tile height must be positive.");
            if (tile.VertexCount != 4)
            {
                throw new InvalidOperationException($"Flat Grid baseline Detour tiles require exactly four NavTile vertices. NavTile {tile.TileId} has {tile.VertexCount}.");
            }

            DtMeshData data = BuildFlatGridBaselineTileData(tile, tileWidthCm, tileHeightCm)
                ?? throw new InvalidOperationException($"Failed to build flat Grid baseline Detour tile data for NavTile {tile.TileId}.");

            return WriteDetourTileBytes(data);
        }

        private static NavPathResult FindPathCore(
            DtNavMesh navMesh,
            NavAreaCostTable areaCosts,
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            int maxPortals)
        {
            var query = new DtNavMeshQuery(navMesh);
            var filter = BuildFilter(areaCosts);
            ref readonly DtNavMeshParams navMeshParams = ref navMesh.GetParams();
            var extents = new RcVec3f(
                MathF.Max(1.0f, navMeshParams.tileWidth * 0.5f),
                256f,
                MathF.Max(1.0f, navMeshParams.tileHeight * 0.5f));

            var start = new RcVec3f(startXcm / 100f, 0f, startZcm / 100f);
            var goal = new RcVec3f(goalXcm / 100f, 0f, goalZcm / 100f);

            var startStatus = query.FindNearestPoly(start, extents, filter, out long startRef, out RcVec3f startPos, out _);
            if (startStatus.Failed() || startRef == 0)
            {
                return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }

            var goalStatus = query.FindNearestPoly(goal, extents, filter, out long goalRef, out RcVec3f goalPos, out _);
            if (goalStatus.Failed() || goalRef == 0)
            {
                return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }

            int pathCapacity = Math.Clamp(maxPortals <= 0 ? 256 : maxPortals, 2, 4096);
            if (TryFindDirectRaycastPath(query, filter, startRef, goalRef, startPos, goalPos, pathCapacity, out NavPathResult directResult))
            {
                return directResult;
            }

            long[] pathRefs = new long[pathCapacity];
            var pathStatus = query.FindPath(startRef, goalRef, startPos, goalPos, filter, pathRefs.AsSpan(), out int pathCount, pathCapacity);
            if (pathStatus.Failed() || pathStatus.IsPartial() || pathCount <= 0 || pathRefs[pathCount - 1] != goalRef)
            {
                return new NavPathResult(NavPathStatus.NotReachable, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }

            var straight = new DtStraightPath[pathCapacity];
            var straightStatus = query.FindStraightPath(
                startPos,
                goalPos,
                pathRefs.AsSpan(0, pathCount),
                pathCount,
                straight.AsSpan(),
                out int straightCount,
                pathCapacity,
                0);
            if (straightStatus.Failed() || straightCount <= 0)
            {
                return new NavPathResult(NavPathStatus.NotReachable, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }

            return BuildPathResult(straight, straightCount);
        }

        private static bool TryFindDirectRaycastPath(
            DtNavMeshQuery query,
            IDtQueryFilter filter,
            long startRef,
            long goalRef,
            RcVec3f startPos,
            RcVec3f goalPos,
            int pathCapacity,
            out NavPathResult result)
        {
            result = default;
            long[] rayPath = new long[pathCapacity];
            DtStatus rayStatus = query.Raycast(
                startRef,
                startPos,
                goalPos,
                filter,
                out float t,
                out _,
                rayPath.AsSpan(),
                out int rayPathCount,
                pathCapacity);
            if (rayStatus.Failed() ||
                rayStatus.IsPartial() ||
                rayStatus.Has(DtStatus.DT_BUFFER_TOO_SMALL) ||
                rayPathCount <= 0 ||
                rayPath[rayPathCount - 1] != goalRef ||
                t < 1.0f)
            {
                return false;
            }

            result = BuildDirectPathResult(startPos, goalPos);
            return true;
        }

        private static DtNavMesh? BuildNavMeshFromDetourTiles(List<DtMeshData> tiles)
        {
            float tileWidth = 0f;
            float tileHeight = 0f;
            float originX = 0f;
            float originZ = 0f;
            int maxPolys = 0;
            bool hasOrigin = false;

            for (int i = 0; i < tiles.Count; i++)
            {
                DtMeshData data = tiles[i];
                float width = data.header.bmax.X - data.header.bmin.X;
                float height = data.header.bmax.Z - data.header.bmin.Z;
                if (width <= 0f || height <= 0f)
                {
                    return null;
                }

                if (tileWidth <= 0f)
                {
                    tileWidth = width;
                    tileHeight = height;
                }

                originX = hasOrigin ? MathF.Min(originX, data.header.bmin.X - data.header.x * tileWidth) : data.header.bmin.X - data.header.x * tileWidth;
                originZ = hasOrigin ? MathF.Min(originZ, data.header.bmin.Z - data.header.y * tileHeight) : data.header.bmin.Z - data.header.y * tileHeight;
                hasOrigin = true;
                maxPolys = Math.Max(maxPolys, data.header.polyCount);
            }

            var navMeshParams = new DtNavMeshParams
            {
                orig = new RcVec3f(originX, 0f, originZ),
                tileWidth = tileWidth,
                tileHeight = tileHeight,
                maxTiles = Math.Max(1, tiles.Count),
                maxPolys = Math.Max(1, maxPolys)
            };

            var navMesh = new DtNavMesh();
            var initStatus = navMesh.Init(navMeshParams, DtDetour.DT_VERTS_PER_POLYGON);
            if (initStatus.Failed())
            {
                return null;
            }

            for (int i = 0; i < tiles.Count; i++)
            {
                var addStatus = navMesh.AddTile(tiles[i], 0, 0, out _);
                if (addStatus.Failed())
                {
                    return null;
                }
            }

            return navMesh;
        }

        private static DtNavMesh? BuildNavMesh(NavTile[] tiles, int layer, int tileWidthCm, int tileHeightCm)
        {
            var filtered = new List<NavTile>(tiles.Length);
            int maxPolys = 0;
            long baseOriginXcm = long.MaxValue;
            long baseOriginZcm = long.MaxValue;
            for (int i = 0; i < tiles.Length; i++)
            {
                NavTile tile = tiles[i];
                if (tile == null || tile.TileId.Layer != layer || tile.TriangleCount == 0 || tile.VertexCount == 0)
                {
                    continue;
                }

                filtered.Add(tile);
                maxPolys = Math.Max(maxPolys, tile.TriangleCount);
                baseOriginXcm = Math.Min(baseOriginXcm, (long)tile.OriginXcm - (long)tile.TileId.ChunkX * tileWidthCm);
                baseOriginZcm = Math.Min(baseOriginZcm, (long)tile.OriginZcm - (long)tile.TileId.ChunkY * tileHeightCm);
            }

            if (filtered.Count == 0 || maxPolys == 0)
            {
                return null;
            }

            // Detour 一边按 floor((pos-orig)/tileSize) 反算 tile 槽位、一边按 header 的 tileX/tileZ 落槽，
            // orig 必须是 tile(0,0) 的世界原点（= tile 原点 − tileIndex×tileSize），不能用 tile 自身的世界原点，
            // 否则非零 tile 坐标被平移两次；与 BuildNavMeshFromDetourTiles 的基原点算法保持一致
            var navMeshParams = new DtNavMeshParams
            {
                orig = new RcVec3f(baseOriginXcm / 100f, 0f, baseOriginZcm / 100f),
                tileWidth = tileWidthCm / 100f,
                tileHeight = tileHeightCm / 100f,
                maxTiles = Math.Max(1, filtered.Count),
                maxPolys = maxPolys
            };

            var navMesh = new DtNavMesh();
            var initStatus = navMesh.Init(navMeshParams, Nvp);
            if (initStatus.Failed())
            {
                return null;
            }

            for (int i = 0; i < filtered.Count; i++)
            {
                DtMeshData data = BuildTileData(filtered[i], tileWidthCm, tileHeightCm);
                if (data == null)
                {
                    continue;
                }

                var addStatus = navMesh.AddTile(data, 0, 0, out _);
                if (addStatus.Failed())
                {
                    return null;
                }
            }

            return navMesh;
        }

        private static DtMeshData BuildTileData(NavTile tile, int tileWidthCm, int tileHeightCm)
        {
            int minYcm = int.MaxValue;
            int maxYcm = int.MinValue;
            for (int i = 0; i < tile.VertexCount; i++)
            {
                minYcm = Math.Min(minYcm, tile.VertexYcm[i]);
                maxYcm = Math.Max(maxYcm, tile.VertexYcm[i]);
            }

            int[] verts = new int[tile.VertexCount * 3];
            for (int i = 0; i < tile.VertexCount; i++)
            {
                int dst = i * 3;
                verts[dst + 0] = tile.VertexXcm[i];
                verts[dst + 1] = tile.VertexYcm[i] - minYcm;
                verts[dst + 2] = tile.VertexZcm[i];
            }

            int[] polys = new int[tile.TriangleCount * Nvp * 2];
            Array.Fill(polys, MeshNullIndex);
            int[] areas = new int[tile.TriangleCount];
            int[] flags = new int[tile.TriangleCount];
            for (int i = 0; i < tile.TriangleCount; i++)
            {
                int src = i * Nvp * 2;
                polys[src + 0] = tile.TriA[i];
                polys[src + 1] = tile.TriB[i];
                polys[src + 2] = tile.TriC[i];
                polys[src + Nvp + 0] = ToDetourNeighbor(tile, i, edge: 0, tileWidthCm, tileHeightCm);
                polys[src + Nvp + 1] = ToDetourNeighbor(tile, i, edge: 1, tileWidthCm, tileHeightCm);
                polys[src + Nvp + 2] = ToDetourNeighbor(tile, i, edge: 2, tileWidthCm, tileHeightCm);

                int area = tile.TriAreaIds[i];
                if (area >= DtDetour.DT_MAX_AREAS)
                {
                    throw new InvalidOperationException($"NavTile {tile.TileId} triangle {i} area id {area} exceeds Detour max area id {DtDetour.DT_MAX_AREAS - 1}.");
                }

                areas[i] = area;
                flags[i] = WalkFlag;
            }

            var option = new DtNavMeshCreateParams
            {
                verts = verts,
                vertCount = tile.VertexCount,
                polys = polys,
                polyAreas = areas,
                polyFlags = flags,
                polyCount = tile.TriangleCount,
                nvp = Nvp,
                walkableHeight = 2f,
                walkableRadius = 0.5f,
                walkableClimb = 0.5f,
                bmin = new RcVec3f(tile.OriginXcm / 100f, minYcm / 100f, tile.OriginZcm / 100f),
                bmax = new RcVec3f((tile.OriginXcm + tileWidthCm) / 100f, maxYcm / 100f, (tile.OriginZcm + tileHeightCm) / 100f),
                cs = QuantizationCellM,
                ch = QuantizationHeightM,
                tileX = tile.TileId.ChunkX,
                tileZ = tile.TileId.ChunkY,
                tileLayer = tile.TileId.Layer,
                buildBvTree = true
            };

            return DtNavMeshBuilder.CreateNavMeshData(option);
        }

        private static DtMeshData BuildFlatGridBaselineTileData(NavTile tile, int tileWidthCm, int tileHeightCm)
        {
            int minYcm = int.MaxValue;
            int maxYcm = int.MinValue;
            for (int i = 0; i < tile.VertexCount; i++)
            {
                minYcm = Math.Min(minYcm, tile.VertexYcm[i]);
                maxYcm = Math.Max(maxYcm, tile.VertexYcm[i]);
            }

            int[] verts = new int[tile.VertexCount * 3];
            for (int i = 0; i < tile.VertexCount; i++)
            {
                int dst = i * 3;
                verts[dst + 0] = tile.VertexXcm[i];
                verts[dst + 1] = tile.VertexYcm[i] - minYcm;
                verts[dst + 2] = tile.VertexZcm[i];
            }

            int[] polys = new int[Nvp * 2];
            Array.Fill(polys, MeshNullIndex);
            polys[0] = 0;
            polys[1] = 3;
            polys[2] = 2;
            polys[3] = 1;
            polys[Nvp + 0] = BorderLink | 0;
            polys[Nvp + 1] = BorderLink | 1;
            polys[Nvp + 2] = BorderLink | 2;
            polys[Nvp + 3] = BorderLink | 3;

            int area = tile.TriAreaIds.Length > 0 ? tile.TriAreaIds[0] : 0;
            if (area >= DtDetour.DT_MAX_AREAS)
            {
                throw new InvalidOperationException($"NavTile {tile.TileId} baseline area id {area} exceeds Detour max area id {DtDetour.DT_MAX_AREAS - 1}.");
            }

            var option = new DtNavMeshCreateParams
            {
                verts = verts,
                vertCount = tile.VertexCount,
                polys = polys,
                polyAreas = new[] { area },
                polyFlags = new[] { WalkFlag },
                polyCount = 1,
                nvp = Nvp,
                walkableHeight = 2f,
                walkableRadius = 0.5f,
                walkableClimb = 0.5f,
                bmin = new RcVec3f(tile.OriginXcm / 100f, minYcm / 100f, tile.OriginZcm / 100f),
                bmax = new RcVec3f((tile.OriginXcm + tileWidthCm) / 100f, maxYcm / 100f, (tile.OriginZcm + tileHeightCm) / 100f),
                cs = QuantizationCellM,
                ch = QuantizationHeightM,
                tileX = tile.TileId.ChunkX,
                tileZ = tile.TileId.ChunkY,
                tileLayer = tile.TileId.Layer,
                buildBvTree = true
            };

            return DtNavMeshBuilder.CreateNavMeshData(option);
        }

        private static byte[] WriteDetourTileBytes(DtMeshData data)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            new DtMeshDataWriter().Write(writer, data, RcByteOrder.LITTLE_ENDIAN, cCompatibility: false);
            writer.Flush();
            return ms.ToArray();
        }

        private static int ToDetourNeighbor(NavTile tile, int triangleIndex, int edge, int tileWidthCm, int tileHeightCm)
        {
            int neighbor = edge == 0 ? tile.N0[triangleIndex] : edge == 1 ? tile.N1[triangleIndex] : tile.N2[triangleIndex];
            if (neighbor >= 0)
            {
                return neighbor;
            }

            GetEdgeVertices(tile, triangleIndex, edge, out int a, out int b);
            int ax = tile.VertexXcm[a];
            int az = tile.VertexZcm[a];
            int bx = tile.VertexXcm[b];
            int bz = tile.VertexZcm[b];

            if (Near(ax, 0) && Near(bx, 0)) return BorderLink | 0;
            if (Near(az, tileHeightCm) && Near(bz, tileHeightCm)) return BorderLink | 1;
            if (Near(ax, tileWidthCm) && Near(bx, tileWidthCm)) return BorderLink | 2;
            if (Near(az, 0) && Near(bz, 0)) return BorderLink | 3;

            return BorderNoPortal;
        }

        private static void GetEdgeVertices(NavTile tile, int triangleIndex, int edge, out int a, out int b)
        {
            if (edge == 0)
            {
                a = tile.TriA[triangleIndex];
                b = tile.TriB[triangleIndex];
            }
            else if (edge == 1)
            {
                a = tile.TriB[triangleIndex];
                b = tile.TriC[triangleIndex];
            }
            else
            {
                a = tile.TriC[triangleIndex];
                b = tile.TriA[triangleIndex];
            }
        }

        private static bool Near(int value, int expected)
        {
            return Math.Abs(value - expected) <= BorderToleranceCm;
        }

        private static DtQueryDefaultFilter BuildFilter(NavAreaCostTable areaCosts)
        {
            float[] costs = new float[DtDetour.DT_MAX_AREAS];
            for (int i = 0; i < costs.Length; i++)
            {
                costs[i] = MathF.Max(0.0001f, (float)(areaCosts ?? NavAreaCostTable.CreateDefault()).Get((byte)i).ToDouble());
            }

            return new DtQueryDefaultFilter(WalkFlag, 0, costs);
        }

        private static NavPathResult BuildPathResult(DtStraightPath[] straight, int straightCount)
        {
            var xs = new List<int>(straightCount);
            var zs = new List<int>(straightCount);
            Fix64 travelCost = Fix64.Zero;
            int prevX = 0;
            int prevZ = 0;
            bool hasPrev = false;

            for (int i = 0; i < straightCount; i++)
            {
                int x = (int)MathF.Round(straight[i].pos.X * 100f);
                int z = (int)MathF.Round(straight[i].pos.Z * 100f);
                if (xs.Count > 0 && xs[xs.Count - 1] == x && zs[zs.Count - 1] == z)
                {
                    continue;
                }

                if (hasPrev)
                {
                    float dx = x - prevX;
                    float dz = z - prevZ;
                    travelCost += Fix64.FromFloat(MathF.Sqrt(dx * dx + dz * dz));
                }

                xs.Add(x);
                zs.Add(z);
                prevX = x;
                prevZ = z;
                hasPrev = true;
            }

            return new NavPathResult(NavPathStatus.Ok, xs.ToArray(), zs.ToArray(), travelCost);
        }

        private static NavPathResult BuildDirectPathResult(RcVec3f startPos, RcVec3f goalPos)
        {
            int startXcm = (int)MathF.Round(startPos.X * 100f);
            int startZcm = (int)MathF.Round(startPos.Z * 100f);
            int goalXcm = (int)MathF.Round(goalPos.X * 100f);
            int goalZcm = (int)MathF.Round(goalPos.Z * 100f);

            if (startXcm == goalXcm && startZcm == goalZcm)
            {
                return new NavPathResult(
                    NavPathStatus.Ok,
                    new[] { startXcm },
                    new[] { startZcm },
                    Fix64.Zero);
            }

            float dx = goalXcm - startXcm;
            float dz = goalZcm - startZcm;
            return new NavPathResult(
                NavPathStatus.Ok,
                new[] { startXcm, goalXcm },
                new[] { startZcm, goalZcm },
                Fix64.FromFloat(MathF.Sqrt(dx * dx + dz * dz)));
        }
    }
}
