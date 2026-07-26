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
        private const float DirectRaycastNudgeMeters = 0.05f;
        private const float SampledDirectHopMeters = 2.0f;
        private const float SampledDirectSnapTolMeters = 0.75f;

        public static NavPathResult FindPath(
            NavTile[] tiles,
            int layer,
            NavAreaCostTable areaCosts,
            int tileWidthCm,
            int tileHeightCm,
            int startXcm,
            int startZcm,
            int goalXcm,
            int goalZcm,
            int maxPortals)
        {
            if (tiles == null || tiles.Length == 0)
            {
                return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }

            var navMesh = BuildNavMesh(tiles, layer, tileWidthCm, tileHeightCm);
            if (navMesh == null)
            {
                return new NavPathResult(NavPathStatus.NotReady, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }

            return FindPath(navMesh, areaCosts, startXcm, startZcm, goalXcm, goalZcm, maxPortals);
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
            NavBorderPortalCoordinateContract.RequireTileExtentFitsPortalCoordinates(
                tileWidthCm,
                tileHeightCm,
                "DetourNavQueryEngine.BuildDetourTileBytes");

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

        private static NavPathResult FindPath(
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
            // Long Raycast can die on hole-annulus fan diagonals even when the geometric segment
            // is covered. Sample short hops along the segment; if every hop is clear, take direct.
            if (TryFindDirectRaycastPath(query, filter, startRef, goalRef, startPos, goalPos, pathCapacity, out NavPathResult directResult) ||
                TryFindSampledDirectPath(query, filter, extents, startPos, goalPos, pathCapacity, out directResult))
            {
                return directResult;
            }

            long[] pathRefs = new long[pathCapacity];
            var pathStatus = query.FindPath(startRef, goalRef, startPos, goalPos, filter, pathRefs.AsSpan(), out int pathCount, pathCapacity);
            if (pathStatus.Failed() || pathStatus.IsPartial() || pathCount <= 0 || pathRefs[pathCount - 1] != goalRef)
            {
                return new NavPathResult(NavPathStatus.NotReachable, Array.Empty<int>(), Array.Empty<int>(), Fix64.Zero);
            }

            if (TryFindSampledDirectPath(query, filter, extents, startPos, goalPos, pathCapacity, out directResult))
            {
                return directResult;
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

        private static bool TryFindSampledDirectPath(
            DtNavMeshQuery query,
            IDtQueryFilter filter,
            RcVec3f extents,
            RcVec3f startPos,
            RcVec3f goalPos,
            int pathCapacity,
            out NavPathResult result)
        {
            result = default;
            float dx = goalPos.X - startPos.X;
            float dy = goalPos.Y - startPos.Y;
            float dz = goalPos.Z - startPos.Z;
            float len = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            if (len <= DirectRaycastNudgeMeters * 2f)
            {
                return false;
            }

            int hops = Math.Max(2, (int)MathF.Ceiling(len / SampledDirectHopMeters));
            long[] rayPath = new long[pathCapacity];
            // Tight XY extents: tile-half extents let FindNearestPoly jump onto fan polys far off-axis.
            var tightExtents = new RcVec3f(
                MathF.Min(extents.X, SampledDirectSnapTolMeters),
                extents.Y,
                MathF.Min(extents.Z, SampledDirectSnapTolMeters));
            RcVec3f prevPos = startPos;
            var prevStatus = query.FindNearestPoly(prevPos, tightExtents, filter, out long prevRef, out RcVec3f prevOnMesh, out _);
            if (prevStatus.Failed() || prevRef == 0)
            {
                return false;
            }

            prevPos = prevOnMesh;
            for (int h = 1; h <= hops; h++)
            {
                float t = h / (float)hops;
                var sample = new RcVec3f(
                    startPos.X + (dx * t),
                    startPos.Y + (dy * t),
                    startPos.Z + (dz * t));
                var sampleStatus = query.FindNearestPoly(sample, tightExtents, filter, out long sampleRef, out RcVec3f sampleOnMesh, out _);
                if (sampleStatus.Failed() || sampleRef == 0)
                {
                    return false;
                }

                // Reject snaps that wandered far off the authored segment (blocked / wrong poly).
                float snapDx = sampleOnMesh.X - sample.X;
                float snapDz = sampleOnMesh.Z - sample.Z;
                if (((snapDx * snapDx) + (snapDz * snapDz)) > SampledDirectSnapTolMeters * SampledDirectSnapTolMeters)
                {
                    return false;
                }

                DtStatus rayStatus = query.Raycast(
                    prevRef,
                    prevPos,
                    sampleOnMesh,
                    filter,
                    out float rayT,
                    out _,
                    rayPath.AsSpan(),
                    out int rayPathCount,
                    pathCapacity);
                if (rayStatus.Failed() ||
                    rayStatus.IsPartial() ||
                    rayStatus.Has(DtStatus.DT_BUFFER_TOO_SMALL) ||
                    rayPathCount <= 0 ||
                    rayT < 1.0f)
                {
                    return false;
                }

                prevRef = sampleRef;
                prevPos = sampleOnMesh;
            }

            result = BuildDirectPathResult(startPos, goalPos);
            return true;
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
            int gridOriginXcm = 0;
            int gridOriginZcm = 0;
            bool hasGridOrigin = false;
            for (int i = 0; i < tiles.Length; i++)
            {
                NavTile tile = tiles[i];
                if (tile == null || tile.TileId.Layer != layer || tile.TriangleCount == 0 || tile.VertexCount == 0)
                {
                    continue;
                }

                // Detour places tile (tileX,tileZ) at orig + (tileX*width, tileZ*height).
                // NavTile.Chunk* is absolute in the triangle-surface tile space, so orig must be that
                // space's world origin — never the min loaded-tile origin (which misplaces non-zero chunks).
                int derivedOriginXcm = checked(tile.OriginXcm - (tile.TileId.ChunkX * tileWidthCm));
                int derivedOriginZcm = checked(tile.OriginZcm - (tile.TileId.ChunkY * tileHeightCm));
                if (!hasGridOrigin)
                {
                    gridOriginXcm = derivedOriginXcm;
                    gridOriginZcm = derivedOriginZcm;
                    hasGridOrigin = true;
                }
                else if (derivedOriginXcm != gridOriginXcm || derivedOriginZcm != gridOriginZcm)
                {
                    throw new InvalidOperationException(
                        $"DetourNavQueryEngine.BuildNavMesh requires a single tile-space origin across loaded tiles. " +
                        $"Tile {tile.TileId} derives origin ({derivedOriginXcm},{derivedOriginZcm}) but batch origin is ({gridOriginXcm},{gridOriginZcm}).");
                }

                filtered.Add(tile);
                maxPolys = Math.Max(maxPolys, tile.TriangleCount);
            }

            if (filtered.Count == 0 || maxPolys == 0)
            {
                return null;
            }

            var navMeshParams = new DtNavMeshParams
            {
                orig = new RcVec3f(gridOriginXcm / 100f, 0f, gridOriginZcm / 100f),
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
                NavTile tile = filtered[i];
                // Same SSOT as Editor Bridge flat-grid-baseline-v2: one convex Detour poly per tile
                // with geometric BorderLinks. Dense LayeredSpan tiles keep BuildTileData.
                DtMeshData data = DefaultGridNavTileFactory.MatchesFlatBaselineFootprint(tile, tileWidthCm, tileHeightCm)
                    ? BuildFlatGridBaselineTileData(tile, tileWidthCm, tileHeightCm)
                    : BuildTileData(tile, tileWidthCm, tileHeightCm);
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

                // Active triangle span only — banked unused TriAreaIds slots are poison and must be ignored.
                int area = tile.ActiveTriAreaIds[i];
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

            // Bank capacity must not affect area semantics: only the active triangle span is readable.
            ReadOnlySpan<byte> activeAreas = tile.ActiveTriAreaIds;
            int area = activeAreas.Length > 0 ? activeAreas[0] : 0;
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
            int ay = tile.VertexYcm[a];
            int az = tile.VertexZcm[a];
            int bx = tile.VertexXcm[b];
            int by = tile.VertexYcm[b];
            int bz = tile.VertexZcm[b];

            // NavBorderPortal is the sole external-link gate. Recast walkable erosion may inset open
            // edges from the geometric tile border; portal overlap still proves the cross-tile link.
            if (!TryMatchAcceptedPortal(
                    tile,
                    ax, ay, az,
                    bx, by, bz,
                    tileWidthCm,
                    tileHeightCm,
                    out int detourDir))
            {
                return BorderNoPortal;
            }

            return BorderLink | detourDir;
        }

        private static bool TryMatchAcceptedPortal(
            NavTile tile,
            int ax,
            int ay,
            int az,
            int bx,
            int by,
            int bz,
            int tileWidthCm,
            int tileHeightCm,
            out int detourDir)
        {
            // When the edge sits on the geometric tile border, only that side may claim it.
            // First-portal-wins at four-tile corners otherwise flips West/North and routes
            // open north marches through world (±6400,0)/(0,±6400).
            bool hasGeometricSide = TryGetBoundarySide(
                ax,
                az,
                bx,
                bz,
                tileWidthCm,
                tileHeightCm,
                out NavPortalSide geometricSide,
                out int geometricDir);

            ReadOnlySpan<NavBorderPortal> portals = tile.ActivePortals;
            int bestOverlap = 0;
            int bestDir = 0;
            bool found = false;
            for (int i = 0; i < portals.Length; i++)
            {
                NavBorderPortal portal = portals[i];
                if (hasGeometricSide && portal.Side != geometricSide)
                {
                    continue;
                }

                if (!EdgeAlignedWithPortalSideBand(portal, ax, az, bx, bz, tileWidthCm, tileHeightCm))
                {
                    continue;
                }

                if (!TryMeasureAcceptedPortalOverlap(
                        tile,
                        portal.Side,
                        ax,
                        ay,
                        az,
                        bx,
                        by,
                        bz,
                        out int overlapCm))
                {
                    continue;
                }

                int dir = portal.Side switch
                {
                    NavPortalSide.West => 0,
                    NavPortalSide.South => 1,
                    NavPortalSide.East => 2,
                    NavPortalSide.North => 3,
                    _ => throw new InvalidOperationException($"Unknown NavPortalSide '{portal.Side}'.")
                };

                if (!found || overlapCm > bestOverlap)
                {
                    found = true;
                    bestOverlap = overlapCm;
                    bestDir = dir;
                }
            }

            if (found)
            {
                detourDir = bestDir;
                return true;
            }

            // Geometric border edge with no portal overlap stays unlinked.
            // Recast-eroded inset edges (not on the geometric border) keep portal-band matching above.
            if (hasGeometricSide)
            {
                detourDir = geometricDir;
                return false;
            }

            detourDir = 0;
            return false;
        }

        private static bool EdgeAlignedWithPortalSideBand(
            in NavBorderPortal portal,
            int ax,
            int az,
            int bx,
            int bz,
            int tileWidthCm,
            int tileHeightCm)
        {
            // Geometric inset band only — half the portal's positive along-span (Recast/CDT contract).
            // NavBorderPortal.ClearanceCm is agent walkability / radius-field headroom (LayeredSpan),
            // not edge-to-boundary tolerance. Feeding it here lets open-floor clearances span a full
            // tile and falsely match opposite-side portals at four-tile corners (±6400 seams).
            GetAlongRange(
                portal.Side,
                portal.LeftXcm,
                portal.LeftZcm,
                portal.RightXcm,
                portal.RightZcm,
                out int portalMinAlong,
                out int portalMaxAlong);
            int alongSpan = portalMaxAlong - portalMinAlong;
            if (alongSpan <= 0)
            {
                // Point/corner contact is never a portal band.
                return false;
            }

            int band = Math.Max(BorderToleranceCm, alongSpan / 2);
            if (portal.Side is NavPortalSide.West or NavPortalSide.East)
            {
                if (Math.Abs(ax - bx) > BorderToleranceCm)
                {
                    return false;
                }

                int boundary = portal.Side == NavPortalSide.West ? 0 : tileWidthCm;
                return Near(ax, boundary, band) && Near(bx, boundary, band);
            }

            if (Math.Abs(az - bz) > BorderToleranceCm)
            {
                return false;
            }

            int boundaryZ = portal.Side == NavPortalSide.North ? 0 : tileHeightCm;
            return Near(az, boundaryZ, band) && Near(bz, boundaryZ, band);
        }

        private static bool Near(int value, int expected, int tolerance)
            => Math.Abs(value - expected) <= tolerance;

        private static bool TryGetBoundarySide(
            int ax,
            int az,
            int bx,
            int bz,
            int tileWidthCm,
            int tileHeightCm,
            out NavPortalSide side,
            out int detourDir)
        {
            // Classify by dominant edge axis first. Checking West before North lets a short
            // north-border edge near x=0 (both X within BorderToleranceCm) steal West and
            // emit the wrong Detour external-link direction at four-tile corners.
            // Detour external link dirs: 0=-X, 1=+Z, 2=+X, 3=-Z.
            // NavPortalSide mapping: West(-X), South(+Z), East(+X), North(-Z).
            int spanX = Math.Abs(ax - bx);
            int spanZ = Math.Abs(az - bz);
            if (spanZ > spanX)
            {
                // Vertical-dominant → West/East only.
                if (Near(ax, 0) && Near(bx, 0))
                {
                    side = NavPortalSide.West;
                    detourDir = 0;
                    return true;
                }

                if (Near(ax, tileWidthCm) && Near(bx, tileWidthCm))
                {
                    side = NavPortalSide.East;
                    detourDir = 2;
                    return true;
                }
            }
            else if (spanX > spanZ)
            {
                // Horizontal-dominant → North/South only.
                if (Near(az, tileHeightCm) && Near(bz, tileHeightCm))
                {
                    side = NavPortalSide.South;
                    detourDir = 1;
                    return true;
                }

                if (Near(az, 0) && Near(bz, 0))
                {
                    side = NavPortalSide.North;
                    detourDir = 3;
                    return true;
                }
            }

            // Point/diagonal contact is not a unique geometric border side.
            side = default;
            detourDir = 0;
            return false;
        }

        private static bool HasAcceptedPortalOverlap(
            NavTile tile,
            NavPortalSide side,
            int ax,
            int ay,
            int az,
            int bx,
            int by,
            int bz)
            => TryMeasureAcceptedPortalOverlap(tile, side, ax, ay, az, bx, by, bz, out _);

        private static bool TryMeasureAcceptedPortalOverlap(
            NavTile tile,
            NavPortalSide side,
            int ax,
            int ay,
            int az,
            int bx,
            int by,
            int bz,
            out int overlapCm)
        {
            overlapCm = 0;
            GetAlongRange(side, ax, az, bx, bz, out int edgeMinAlong, out int edgeMaxAlong);
            if (edgeMaxAlong <= edgeMinAlong)
            {
                // Point/corner contact is never a portal.
                return false;
            }

            int edgeMinY = ay < by ? ay : by;
            int edgeMaxY = ay > by ? ay : by;

            ReadOnlySpan<NavBorderPortal> portals = tile.ActivePortals;
            int bestOverlap = 0;
            for (int i = 0; i < portals.Length; i++)
            {
                NavBorderPortal portal = portals[i];
                if (portal.Side != side)
                {
                    continue;
                }

                GetAlongRange(
                    side,
                    portal.LeftXcm,
                    portal.LeftZcm,
                    portal.RightXcm,
                    portal.RightZcm,
                    out int portalMinAlong,
                    out int portalMaxAlong);
                int overlapMin = edgeMinAlong > portalMinAlong ? edgeMinAlong : portalMinAlong;
                int overlapMax = edgeMaxAlong < portalMaxAlong ? edgeMaxAlong : portalMaxAlong;
                int overlap = overlapMax - overlapMin;
                if (overlap <= 0)
                {
                    continue;
                }

                int portalMinY = portal.LeftYcm < portal.RightYcm ? portal.LeftYcm : portal.RightYcm;
                int portalMaxY = portal.LeftYcm > portal.RightYcm ? portal.LeftYcm : portal.RightYcm;
                // Inclusive Y compatibility so coplanar flat portals (min==max) still match.
                if (edgeMinY > portalMaxY + BorderToleranceCm || portalMinY > edgeMaxY + BorderToleranceCm)
                {
                    continue;
                }

                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                }
            }

            if (bestOverlap <= 0)
            {
                return false;
            }

            overlapCm = bestOverlap;
            return true;
        }

        private static void GetAlongRange(
            NavPortalSide side,
            int ax,
            int az,
            int bx,
            int bz,
            out int minAlong,
            out int maxAlong)
        {
            if (side == NavPortalSide.West || side == NavPortalSide.East)
            {
                minAlong = az < bz ? az : bz;
                maxAlong = az > bz ? az : bz;
            }
            else
            {
                minAlong = ax < bx ? ax : bx;
                maxAlong = ax > bx ? ax : bx;
            }
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
            var ys = new List<int>(straightCount);
            var zs = new List<int>(straightCount);
            Fix64 travelCost = Fix64.Zero;
            int prevX = 0;
            int prevY = 0;
            int prevZ = 0;
            bool hasPrev = false;

            for (int i = 0; i < straightCount; i++)
            {
                int x = (int)MathF.Round(straight[i].pos.X * 100f);
                int y = (int)MathF.Round(straight[i].pos.Y * 100f);
                int z = (int)MathF.Round(straight[i].pos.Z * 100f);
                if (xs.Count > 0 && xs[xs.Count - 1] == x && ys[ys.Count - 1] == y && zs[zs.Count - 1] == z)
                {
                    continue;
                }

                if (hasPrev)
                {
                    float dx = x - prevX;
                    float dy = y - prevY;
                    float dz = z - prevZ;
                    travelCost += Fix64.FromFloat(MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz)));
                }

                xs.Add(x);
                ys.Add(y);
                zs.Add(z);
                prevX = x;
                prevY = y;
                prevZ = z;
                hasPrev = true;
            }

            return new NavPathResult(NavPathStatus.Ok, xs.ToArray(), ys.ToArray(), zs.ToArray(), travelCost);
        }

        private static NavPathResult BuildDirectPathResult(RcVec3f startPos, RcVec3f goalPos)
        {
            int startXcm = (int)MathF.Round(startPos.X * 100f);
            int startYcm = (int)MathF.Round(startPos.Y * 100f);
            int startZcm = (int)MathF.Round(startPos.Z * 100f);
            int goalXcm = (int)MathF.Round(goalPos.X * 100f);
            int goalYcm = (int)MathF.Round(goalPos.Y * 100f);
            int goalZcm = (int)MathF.Round(goalPos.Z * 100f);

            if (startXcm == goalXcm && startYcm == goalYcm && startZcm == goalZcm)
            {
                return new NavPathResult(
                    NavPathStatus.Ok,
                    new[] { startXcm },
                    new[] { startYcm },
                    new[] { startZcm },
                    Fix64.Zero);
            }

            float dx = goalXcm - startXcm;
            float dy = goalYcm - startYcm;
            float dz = goalZcm - startZcm;
            return new NavPathResult(
                NavPathStatus.Ok,
                new[] { startXcm, goalXcm },
                new[] { startYcm, goalYcm },
                new[] { startZcm, goalZcm },
                Fix64.FromFloat(MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz))));
        }
    }
}
