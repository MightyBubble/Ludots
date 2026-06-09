using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LogicHeightmap;

namespace Ludots.NavBake.Recast
{
    public static class RecastNavTileBaker
    {
        private static RcConfig BuildRcConfig(NavAgentProfileConfig profile)
        {
            float radius = profile.RadiusCm / 100f;
            float height = profile.HeightCm / 100f;
            float maxClimb = profile.MaxClimbCm / 100f;
            float maxSlope = profile.MaxSlopeDeg;

            float cellSize = MathF.Max(1.0f, MathF.Min(2.0f, radius * 2.0f));
            float cellHeight = 0.5f;

            return new RcConfig(
                DotRecast.Recast.RcPartition.MONOTONE,
                cellSize, cellHeight,
                maxSlope, height, radius, maxClimb,
                2, 8,
                24f, 1.8f,
                6,
                4f, 1f,
                true, true, true,
                new RcAreaModification(RcRecast.RC_WALKABLE_AREA), true);
        }

        private static void BuildRecastTriangleMesh(NavTile baseTile, NavObstacleSet obstacles, int layer, out List<float> verts, out List<int> tris)
        {
            int vCount = baseTile.VertexCount;
            verts = new List<float>(vCount * 3);
            for (int i = 0; i < vCount; i++)
            {
                verts.Add((baseTile.OriginXcm + baseTile.VertexXcm[i]) / 100f);
                verts.Add(baseTile.VertexYcm[i] / 100f);
                verts.Add((baseTile.OriginZcm + baseTile.VertexZcm[i]) / 100f);
            }

            tris = new List<int>(baseTile.TriangleCount * 3);
            for (int i = 0; i < baseTile.TriangleCount; i++)
            {
                int a = baseTile.TriA[i];
                int b = baseTile.TriB[i];
                int c = baseTile.TriC[i];

                if (IsTriangleBlockedByObstacles(baseTile, a, b, c, obstacles, layer))
                {
                    continue;
                }

                if (ComputeTriangleNormalY(baseTile, a, b, c) < 0)
                {
                    tris.Add(a);
                    tris.Add(c);
                    tris.Add(b);
                }
                else
                {
                    tris.Add(a);
                    tris.Add(b);
                    tris.Add(c);
                }
            }
        }

        private static long ComputeTriangleNormalY(NavTile tile, int a, int b, int c)
        {
            long ax = tile.VertexXcm[a];
            long az = tile.VertexZcm[a];
            long bx = tile.VertexXcm[b];
            long bz = tile.VertexZcm[b];
            long cx = tile.VertexXcm[c];
            long cz = tile.VertexZcm[c];

            long abx = bx - ax;
            long abz = bz - az;
            long acx = cx - ax;
            long acz = cz - az;
            return (abz * acx) - (abx * acz);
        }

        private static bool IsTriangleBlockedByObstacles(NavTile tile, int a, int b, int c, NavObstacleSet obstacles, int layer)
        {
            if (obstacles?.Obstacles == null || obstacles.Obstacles.Count == 0) return false;

            int ax = tile.OriginXcm + tile.VertexXcm[a];
            int az = tile.OriginZcm + tile.VertexZcm[a];
            int bx = tile.OriginXcm + tile.VertexXcm[b];
            int bz = tile.OriginZcm + tile.VertexZcm[b];
            int cx = tile.OriginXcm + tile.VertexXcm[c];
            int cz = tile.OriginZcm + tile.VertexZcm[c];

            int mx = (ax + bx + cx) / 3;
            int mz = (az + bz + cz) / 3;

            for (int i = 0; i < obstacles.Obstacles.Count; i++)
            {
                var o = obstacles.Obstacles[i];
                if (!o.Enabled) continue;
                if (ResolveLayerId(o.LayerId) != layer) continue;

                if (o.Kind == NavObstacleKind.Circle)
                {
                    if (CircleIntersectsTriangle(o.Center.Xcm, o.Center.Zcm, o.RadiusCm, ax, az, bx, bz, cx, cz) ||
                        PointInsideCircle(mx, mz, o.Center.Xcm, o.Center.Zcm, o.RadiusCm))
                    {
                        return true;
                    }
                }
                else if (o.Kind == NavObstacleKind.Polygon)
                {
                    if (PolygonIntersectsTriangle(o.Points, ax, az, bx, bz, cx, cz) ||
                        PointInPolygonOrBoundary(mx, mz, o.Points))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool TryBake(
            LogicHeightmap logicHeightmap,
            int chunkX,
            int chunkY,
            uint tileVersion,
            in NavBuildConfig legacyConfig,
            NavAgentProfileConfig profile,
            int layer,
            NavObstacleSet obstacles,
            out NavTile tile,
            out NavBakeArtifact artifact)
        {
            if (logicHeightmap == null)
            {
                tile = null!;
                artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, layer), tileVersion, NavBakeStage.None, NavBakeErrorCode.InvalidInput, "LogicHeightmap is null.", 0, 0, 0, 0);
                return false;
            }

            if (!TryBuildLogicBaseTile(logicHeightmap, chunkX, chunkY, tileVersion, legacyConfig, layer, out var baseTile, out var baseArtifact))
            {
                tile = null!;
                artifact = baseArtifact;
                return false;
            }

            if (baseTile.TriangleCount == 0)
            {
                tile = baseTile;
                artifact = new NavBakeArtifact(tile.TileId, tile.TileVersion, NavBakeStage.Serialize, NavBakeErrorCode.None, "Empty layer-domain tile.", 0, 0, 0, 0);
                return true;
            }

            try
            {
                if (!TryBuildRecastInputTile(
                        logicHeightmap,
                        chunkX,
                        chunkY,
                        tileVersion,
                        legacyConfig,
                        layer,
                        baseTile.OriginXcm,
                        baseTile.OriginZcm,
                        out var recastInputTile,
                        out var recastInputArtifact))
                {
                    tile = null!;
                    artifact = recastInputArtifact;
                    return false;
                }

                BuildRecastTriangleMesh(recastInputTile, obstacles, layer, out var verts, out var tris);
                if (tris.Count == 0)
                {
                    tile = CreateEmptyLogicNavTile(chunkX, chunkY, layer, tileVersion, legacyConfig.ComputeHash(), baseTile.OriginXcm, baseTile.OriginZcm);
                    artifact = new NavBakeArtifact(tile.TileId, tile.TileVersion, NavBakeStage.Serialize, NavBakeErrorCode.None, "Empty tile after obstacle filtering.", 0, 0, 0, 0);
                    return true;
                }

                var geom = new RcSampleInputGeomProvider(verts.ToArray(), tris.ToArray());
                var rcCfg = BuildRcConfig(profile);
                var bcfg = new RcBuilderConfig(rcCfg, geom.GetMeshBoundsMin(), geom.GetMeshBoundsMax());
                var rcBuilder = new RcBuilder();
                var rcResult = rcBuilder.Build(geom, bcfg, keepInterResults: false);

                if (rcResult?.MeshDetail == null || rcResult.MeshDetail.ntris <= 0)
                {
                    string detailMessage = rcResult == null
                        ? "Recast returned null build result."
                        : $"Recast produced empty detail mesh. polyVerts={rcResult.Mesh?.nverts ?? 0} polyCount={rcResult.Mesh?.npolys ?? 0} detailMeshes={rcResult.MeshDetail?.nmeshes ?? 0} detailVerts={rcResult.MeshDetail?.nverts ?? 0}";
                    tile = CreateEmptyLogicNavTile(chunkX, chunkY, layer, tileVersion, legacyConfig.ComputeHash(), baseTile.OriginXcm, baseTile.OriginZcm);
                    artifact = new NavBakeArtifact(tile.TileId, tile.TileVersion, NavBakeStage.Serialize, NavBakeErrorCode.None, detailMessage, baseArtifact.WalkableTriangleCount, 0, 0, 0);
                    return true;
                }

                BuildNavTileFromDetailMesh(
                    baseTile,
                    layer,
                    tileVersion,
                    legacyConfig.ComputeHash(),
                    rcResult.MeshDetail,
                    out tile);

                artifact = new NavBakeArtifact(tile.TileId, tile.TileVersion, NavBakeStage.Serialize, NavBakeErrorCode.None, "", baseArtifact.WalkableTriangleCount, tile.VertexCount, tile.TriangleCount, tile.Portals.Length);
                return true;
            }
            catch (Exception ex)
            {
                tile = null!;
                artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, layer), tileVersion, NavBakeStage.Serialize, NavBakeErrorCode.SerializationFailed, ex.Message, 0, 0, 0, 0);
                return false;
            }
        }

        private readonly struct LogicVertex
        {
            public readonly int SampleX;
            public readonly int SampleY;
            public readonly int Xcm;
            public readonly int Ycm;
            public readonly int Zcm;
            public readonly int WaterYcm;
            public readonly byte AreaId;
            public readonly bool IsBlocked;

            public LogicVertex(int sampleX, int sampleY, int xcm, int ycm, int zcm, int waterYcm, byte areaId, bool isBlocked)
            {
                SampleX = sampleX;
                SampleY = sampleY;
                Xcm = xcm;
                Ycm = ycm;
                Zcm = zcm;
                WaterYcm = waterYcm;
                AreaId = areaId;
                IsBlocked = isBlocked;
            }
        }

        private readonly struct LogicVertexKey : IEquatable<LogicVertexKey>
        {
            public readonly int Xcm;
            public readonly int Ycm;
            public readonly int Zcm;

            public LogicVertexKey(int xcm, int ycm, int zcm)
            {
                Xcm = xcm;
                Ycm = ycm;
                Zcm = zcm;
            }

            public bool Equals(LogicVertexKey other) => Xcm == other.Xcm && Ycm == other.Ycm && Zcm == other.Zcm;
            public override bool Equals(object? obj) => obj is LogicVertexKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Xcm, Ycm, Zcm);
        }

        private static bool TryBuildLogicBaseTile(
            LogicHeightmap logic,
            int chunkX,
            int chunkY,
            uint tileVersion,
            in NavBuildConfig config,
            int layer,
            out NavTile tile,
            out NavBakeArtifact artifact)
        {
            tile = null!;
            artifact = default;

            if (!logic.IsValidChunk(chunkX, chunkY))
            {
                artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, layer), tileVersion, NavBakeStage.None, NavBakeErrorCode.InvalidInput, "LogicHeightmap tile out of range.", 0, 0, 0, 0);
                return false;
            }

            int startX = chunkX * LogicHeightmapChunk.ChunkSize;
            int startY = chunkY * LogicHeightmapChunk.ChunkSize;
            int mapWidth = logic.WidthSamples;
            int mapHeight = logic.HeightSamples;
            int originXcm = GetTileOriginXcm(logic, chunkX, chunkY);
            int originZcm = GetTileOriginZcm(logic, chunkX, chunkY);

            var vertexIndex = new Dictionary<LogicVertexKey, int>(4096);
            var vx = new List<int>(4096);
            var vy = new List<int>(4096);
            var vz = new List<int>(4096);
            var triA = new List<int>(8192);
            var triB = new List<int>(8192);
            var triC = new List<int>(8192);
            var triAreas = new List<byte>(8192);

            int walkableTriCount = 0;
            for (int y = startY; y < startY + LogicHeightmapChunk.ChunkSize; y++)
            {
                for (int x = startX; x < startX + LogicHeightmapChunk.ChunkSize; x++)
                {
                    if (y >= mapHeight - 1 || x >= mapWidth - 1)
                    {
                        continue;
                    }

                    if (logic.GridKind == LogicHeightmapGridKind.HexVertex)
                    {
                        bool isOdd = (y & 1) == 1;
                        if (!isOdd)
                        {
                            AddLogicFace(logic, originXcm, originZcm, config, layer, x, y, x + 1, y, x, y + 1, vertexIndex, vx, vy, vz, triA, triB, triC, triAreas, ref walkableTriCount);
                            AddLogicFace(logic, originXcm, originZcm, config, layer, x + 1, y, x + 1, y + 1, x, y + 1, vertexIndex, vx, vy, vz, triA, triB, triC, triAreas, ref walkableTriCount);
                        }
                        else
                        {
                            AddLogicFace(logic, originXcm, originZcm, config, layer, x, y, x + 1, y, x + 1, y + 1, vertexIndex, vx, vy, vz, triA, triB, triC, triAreas, ref walkableTriCount);
                            AddLogicFace(logic, originXcm, originZcm, config, layer, x, y, x + 1, y + 1, x, y + 1, vertexIndex, vx, vy, vz, triA, triB, triC, triAreas, ref walkableTriCount);
                        }
                    }
                    else
                    {
                        AddLogicFace(logic, originXcm, originZcm, config, layer, x, y, x + 1, y, x, y + 1, vertexIndex, vx, vy, vz, triA, triB, triC, triAreas, ref walkableTriCount);
                        AddLogicFace(logic, originXcm, originZcm, config, layer, x + 1, y, x + 1, y + 1, x, y + 1, vertexIndex, vx, vy, vz, triA, triB, triC, triAreas, ref walkableTriCount);
                    }
                }
            }

            if (triA.Count == 0)
            {
                tile = CreateEmptyLogicNavTile(chunkX, chunkY, layer, tileVersion, config.ComputeHash(), originXcm, originZcm);
                artifact = new NavBakeArtifact(tile.TileId, tile.TileVersion, NavBakeStage.Serialize, NavBakeErrorCode.None, "Empty layer-domain tile.", 0, 0, 0, 0);
                return true;
            }

            var n0 = new int[triA.Count];
            var n1 = new int[triA.Count];
            var n2 = new int[triA.Count];
            Array.Fill(n0, -1);
            Array.Fill(n1, -1);
            Array.Fill(n2, -1);
            BuildAdjacency(triA, triB, triC, n0, n1, n2);

            var portals = BuildLogicPortals(logic, startX, startY, originXcm, originZcm, config, layer);
            tile = new NavTile(
                new NavTileId(chunkX, chunkY, layer),
                tileVersion,
                config.ComputeHash(),
                0UL,
                originXcm,
                originZcm,
                vx.ToArray(),
                vy.ToArray(),
                vz.ToArray(),
                triA.ToArray(),
                triB.ToArray(),
                triC.ToArray(),
                n0,
                n1,
                n2,
                triAreas.ToArray(),
                portals);

            artifact = new NavBakeArtifact(tile.TileId, tile.TileVersion, NavBakeStage.Serialize, NavBakeErrorCode.None, "", walkableTriCount, tile.VertexCount, tile.TriangleCount, tile.Portals.Length);
            return true;
        }

        private static bool TryBuildRecastInputTile(
            LogicHeightmap logic,
            int chunkX,
            int chunkY,
            uint tileVersion,
            in NavBuildConfig config,
            int layer,
            int originXcm,
            int originZcm,
            out NavTile tile,
            out NavBakeArtifact artifact)
        {
            tile = null!;
            artifact = default;

            if (!logic.IsValidChunk(chunkX, chunkY))
            {
                artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, layer), tileVersion, NavBakeStage.None, NavBakeErrorCode.InvalidInput, "LogicHeightmap tile out of range.", 0, 0, 0, 0);
                return false;
            }

            int firstChunkX = Math.Max(0, chunkX - 1);
            int firstChunkY = Math.Max(0, chunkY - 1);
            int lastChunkX = Math.Min(logic.WidthInChunks - 1, chunkX + 1);
            int lastChunkY = Math.Min(logic.HeightInChunks - 1, chunkY + 1);
            int startX = firstChunkX * LogicHeightmapChunk.ChunkSize;
            int startY = firstChunkY * LogicHeightmapChunk.ChunkSize;
            int endX = (lastChunkX + 1) * LogicHeightmapChunk.ChunkSize;
            int endY = (lastChunkY + 1) * LogicHeightmapChunk.ChunkSize;
            int mapWidth = logic.WidthSamples;
            int mapHeight = logic.HeightSamples;

            var vertexIndex = new Dictionary<LogicVertexKey, int>(4096 * 9);
            var vx = new List<int>(4096 * 9);
            var vy = new List<int>(4096 * 9);
            var vz = new List<int>(4096 * 9);
            var triA = new List<int>(8192 * 9);
            var triB = new List<int>(8192 * 9);
            var triC = new List<int>(8192 * 9);
            var triAreas = new List<byte>(8192 * 9);

            int walkableTriCount = 0;
            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    if (y >= mapHeight - 1 || x >= mapWidth - 1)
                    {
                        continue;
                    }

                    if (logic.GridKind == LogicHeightmapGridKind.HexVertex)
                    {
                        bool isOdd = (y & 1) == 1;
                        if (!isOdd)
                        {
                            AddLogicFace(logic, originXcm, originZcm, config, layer, x, y, x + 1, y, x, y + 1, vertexIndex, vx, vy, vz, triA, triB, triC, triAreas, ref walkableTriCount);
                            AddLogicFace(logic, originXcm, originZcm, config, layer, x + 1, y, x + 1, y + 1, x, y + 1, vertexIndex, vx, vy, vz, triA, triB, triC, triAreas, ref walkableTriCount);
                        }
                        else
                        {
                            AddLogicFace(logic, originXcm, originZcm, config, layer, x, y, x + 1, y, x + 1, y + 1, vertexIndex, vx, vy, vz, triA, triB, triC, triAreas, ref walkableTriCount);
                            AddLogicFace(logic, originXcm, originZcm, config, layer, x, y, x + 1, y + 1, x, y + 1, vertexIndex, vx, vy, vz, triA, triB, triC, triAreas, ref walkableTriCount);
                        }
                    }
                    else
                    {
                        AddLogicFace(logic, originXcm, originZcm, config, layer, x, y, x + 1, y, x, y + 1, vertexIndex, vx, vy, vz, triA, triB, triC, triAreas, ref walkableTriCount);
                        AddLogicFace(logic, originXcm, originZcm, config, layer, x + 1, y, x + 1, y + 1, x, y + 1, vertexIndex, vx, vy, vz, triA, triB, triC, triAreas, ref walkableTriCount);
                    }
                }
            }

            if (triA.Count == 0)
            {
                tile = CreateEmptyLogicNavTile(chunkX, chunkY, layer, tileVersion, config.ComputeHash(), originXcm, originZcm);
                artifact = new NavBakeArtifact(tile.TileId, tile.TileVersion, NavBakeStage.Serialize, NavBakeErrorCode.None, "Empty layer-domain Recast input tile.", 0, 0, 0, 0);
                return true;
            }

            var n0 = new int[triA.Count];
            var n1 = new int[triA.Count];
            var n2 = new int[triA.Count];
            Array.Fill(n0, -1);
            Array.Fill(n1, -1);
            Array.Fill(n2, -1);
            BuildAdjacency(triA, triB, triC, n0, n1, n2);

            tile = new NavTile(
                new NavTileId(chunkX, chunkY, layer),
                tileVersion,
                config.ComputeHash(),
                0UL,
                originXcm,
                originZcm,
                vx.ToArray(),
                vy.ToArray(),
                vz.ToArray(),
                triA.ToArray(),
                triB.ToArray(),
                triC.ToArray(),
                n0,
                n1,
                n2,
                triAreas.ToArray(),
                Array.Empty<NavBorderPortal>());

            artifact = new NavBakeArtifact(tile.TileId, tile.TileVersion, NavBakeStage.Serialize, NavBakeErrorCode.None, "", walkableTriCount, tile.VertexCount, tile.TriangleCount, 0);
            return true;
        }

        private static NavTile CreateEmptyLogicNavTile(
            int chunkX,
            int chunkY,
            int layer,
            uint tileVersion,
            ulong buildHash,
            int originXcm,
            int originZcm)
        {
            return new NavTile(
                new NavTileId(chunkX, chunkY, layer),
                tileVersion,
                buildHash,
                0UL,
                originXcm,
                originZcm,
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<int>(),
                Array.Empty<byte>(),
                Array.Empty<NavBorderPortal>());
        }

        private static void AddLogicFace(
            LogicHeightmap logic,
            int originXcm,
            int originZcm,
            in NavBuildConfig config,
            int layer,
            int ax,
            int ay,
            int bx,
            int by,
            int cx,
            int cy,
            Dictionary<LogicVertexKey, int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz,
            List<int> triA,
            List<int> triB,
            List<int> triC,
            List<byte> triAreas,
            ref int walkableTriCount)
        {
            if (!TryGetLogicVertex(logic, ax, ay, out var a) ||
                !TryGetLogicVertex(logic, bx, by, out var b) ||
                !TryGetLogicVertex(logic, cx, cy, out var c))
            {
                return;
            }

            if (!IsLogicTriangleWalkable(a, b, c, config, layer))
            {
                return;
            }

            int ia = GetOrAddLogicVertex(a, originXcm, originZcm, vertexIndex, vx, vy, vz);
            int ib = GetOrAddLogicVertex(b, originXcm, originZcm, vertexIndex, vx, vy, vz);
            int ic = GetOrAddLogicVertex(c, originXcm, originZcm, vertexIndex, vx, vy, vz);
            if (ia == ib || ib == ic || ia == ic)
            {
                return;
            }

            triA.Add(ia);
            triB.Add(ib);
            triC.Add(ic);
            triAreas.Add(CombineArea(a.AreaId, b.AreaId, c.AreaId));
            walkableTriCount++;
        }

        private static int GetOrAddLogicVertex(
            in LogicVertex vertex,
            int originXcm,
            int originZcm,
            Dictionary<LogicVertexKey, int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz)
        {
            int localXcm = vertex.Xcm - originXcm;
            int localZcm = vertex.Zcm - originZcm;
            var key = new LogicVertexKey(localXcm, vertex.Ycm, localZcm);
            if (vertexIndex.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int id = vx.Count;
            vertexIndex[key] = id;
            vx.Add(localXcm);
            vy.Add(vertex.Ycm);
            vz.Add(localZcm);
            return id;
        }

        private static bool TryGetLogicVertex(LogicHeightmap logic, int sampleX, int sampleY, out LogicVertex vertex)
        {
            vertex = default;
            if ((uint)sampleX >= (uint)logic.WidthSamples || (uint)sampleY >= (uint)logic.HeightSamples)
            {
                return false;
            }

            var chunk = logic.GetChunk(sampleX, sampleY);
            if (chunk == null)
            {
                return false;
            }

            int localX = sampleX & LogicHeightmapChunk.ChunkSizeMask;
            int localY = sampleY & LogicHeightmapChunk.ChunkSizeMask;
            GetLogicWorldXZCm(logic, sampleX, sampleY, out int xcm, out int zcm);
            vertex = new LogicVertex(
                sampleX,
                sampleY,
                xcm,
                chunk.GetHeightCm(localX, localY),
                zcm,
                chunk.GetWaterHeightCm(localX, localY),
                chunk.GetAreaId(localX, localY),
                chunk.IsBlocked(localX, localY));
            return true;
        }

        private static bool IsLogicTriangleWalkable(in LogicVertex a, in LogicVertex b, in LogicVertex c, in NavBuildConfig config, int layer)
        {
            if (!IsLogicTriangleLayerDomainWalkable(a, b, c, layer))
            {
                return false;
            }

            if (a.WaterYcm > a.Ycm || b.WaterYcm > b.Ycm || c.WaterYcm > c.Ycm)
            {
                return false;
            }

            Vector3 av = new(a.Xcm * 0.01f, a.Ycm * 0.01f, a.Zcm * 0.01f);
            Vector3 bv = new(b.Xcm * 0.01f, b.Ycm * 0.01f, b.Zcm * 0.01f);
            Vector3 cv = new(c.Xcm * 0.01f, c.Ycm * 0.01f, c.Zcm * 0.01f);
            Vector3 normal = Vector3.Cross(bv - av, cv - av);
            float len = normal.Length();
            if (len <= 1e-6f)
            {
                return false;
            }

            normal /= len;
            if (normal.Y < 0f)
            {
                normal = -normal;
            }

            return normal.Y >= config.MinWalkableUpDot;
        }

        private static bool IsLogicTriangleLayerDomainWalkable(in LogicVertex a, in LogicVertex b, in LogicVertex c, int layer)
        {
            return layer switch
            {
                1 => IsWaterVertex(a) && IsWaterVertex(b) && IsWaterVertex(c),
                2 => !IsNoFlyVertex(a) && !IsNoFlyVertex(b) && !IsNoFlyVertex(c),
                3 => IsMountainVertex(a) && IsMountainVertex(b) && IsMountainVertex(c),
                _ => IsGroundVertex(a) && IsGroundVertex(b) && IsGroundVertex(c)
            };
        }

        private static bool IsGroundVertex(in LogicVertex vertex)
        {
            return !vertex.IsBlocked &&
                vertex.WaterYcm <= vertex.Ycm &&
                vertex.AreaId != 5;
        }

        private static bool IsWaterVertex(in LogicVertex vertex)
        {
            return !vertex.IsBlocked &&
                (vertex.AreaId == 4 || vertex.AreaId == 5 || vertex.WaterYcm > vertex.Ycm);
        }

        private static bool IsNoFlyVertex(in LogicVertex vertex)
        {
            return vertex.IsBlocked || vertex.AreaId == 6;
        }

        private static bool IsMountainVertex(in LogicVertex vertex)
        {
            return !vertex.IsBlocked &&
                vertex.WaterYcm <= vertex.Ycm &&
                (vertex.AreaId == 0 || vertex.AreaId == 1 || vertex.AreaId == 2 || vertex.AreaId == 3);
        }

        private static byte CombineArea(byte a, byte b, byte c)
        {
            return Math.Max(a, Math.Max(b, c));
        }

        private static int GetTileOriginXcm(LogicHeightmap logic, int chunkX, int chunkY)
        {
            int sampleX = chunkX * LogicHeightmapChunk.ChunkSize;
            int sampleY = chunkY * LogicHeightmapChunk.ChunkSize;
            GetLogicWorldXZCm(logic, sampleX, sampleY, out int xcm, out _);
            return xcm;
        }

        private static int GetTileOriginZcm(LogicHeightmap logic, int chunkX, int chunkY)
        {
            int sampleX = chunkX * LogicHeightmapChunk.ChunkSize;
            int sampleY = chunkY * LogicHeightmapChunk.ChunkSize;
            GetLogicWorldXZCm(logic, sampleX, sampleY, out _, out int zcm);
            return zcm;
        }

        private static void GetLogicWorldXZCm(LogicHeightmap logic, int sampleX, int sampleY, out int xcm, out int zcm)
        {
            if (logic.GridKind == LogicHeightmapGridKind.HexVertex)
            {
                xcm = (int)MathF.Round(HexCoordinates.HexWidth * 100f * (sampleX + 0.5f * (sampleY & 1)));
                zcm = (int)MathF.Round(HexCoordinates.RowSpacing * 100f * sampleY);
                return;
            }

            xcm = checked(sampleX * logic.CellSizeXCm);
            zcm = checked(sampleY * logic.CellSizeZCm);
        }

        private static NavBorderPortal[] BuildLogicPortals(
            LogicHeightmap logic,
            int startX,
            int startY,
            int originXcm,
            int originZcm,
            in NavBuildConfig config,
            int layer)
        {
            var portals = new List<NavBorderPortal>(64);
            int endX = startX + LogicHeightmapChunk.ChunkSize;
            int endY = startY + LogicHeightmapChunk.ChunkSize;

            AddLogicVerticalPortals(logic, startX, startY, endY, originXcm, originZcm, config, layer, NavPortalSide.West, insideX: startX, outsideX: startX - 1, portals);
            AddLogicVerticalPortals(logic, endX, startY, endY, originXcm, originZcm, config, layer, NavPortalSide.East, insideX: endX - 1, outsideX: endX, portals);
            AddLogicHorizontalPortals(logic, startY, startX, endX, originXcm, originZcm, config, layer, NavPortalSide.North, insideY: startY, outsideY: startY - 1, portals);
            AddLogicHorizontalPortals(logic, endY, startX, endX, originXcm, originZcm, config, layer, NavPortalSide.South, insideY: endY - 1, outsideY: endY, portals);
            return portals.ToArray();
        }

        private static void AddLogicVerticalPortals(
            LogicHeightmap logic,
            int boundaryX,
            int startY,
            int endY,
            int originXcm,
            int originZcm,
            in NavBuildConfig config,
            int layer,
            NavPortalSide side,
            int insideX,
            int outsideX,
            List<NavBorderPortal> portals)
        {
            int segStart = -1;
            for (int y = startY; y < endY; y++)
            {
                bool passable = IsLogicCellAnyTriangleWalkable(logic, insideX, y, config, layer) &&
                                IsLogicCellAnyTriangleWalkable(logic, outsideX, y, config, layer);
                int localV = y - startY;
                if (passable)
                {
                    if (segStart < 0) segStart = localV;
                }
                else if (segStart >= 0)
                {
                    AddLogicVerticalPortalSegment(logic, boundaryX, startY, segStart, localV, originXcm, originZcm, side, portals);
                    segStart = -1;
                }
            }

            if (segStart >= 0)
            {
                AddLogicVerticalPortalSegment(logic, boundaryX, startY, segStart, endY - startY, originXcm, originZcm, side, portals);
            }
        }

        private static void AddLogicHorizontalPortals(
            LogicHeightmap logic,
            int boundaryY,
            int startX,
            int endX,
            int originXcm,
            int originZcm,
            in NavBuildConfig config,
            int layer,
            NavPortalSide side,
            int insideY,
            int outsideY,
            List<NavBorderPortal> portals)
        {
            int segStart = -1;
            for (int x = startX; x < endX; x++)
            {
                bool passable = IsLogicCellAnyTriangleWalkable(logic, x, insideY, config, layer) &&
                                IsLogicCellAnyTriangleWalkable(logic, x, outsideY, config, layer);
                int localU = x - startX;
                if (passable)
                {
                    if (segStart < 0) segStart = localU;
                }
                else if (segStart >= 0)
                {
                    AddLogicHorizontalPortalSegment(logic, boundaryY, startX, segStart, localU, originXcm, originZcm, side, portals);
                    segStart = -1;
                }
            }

            if (segStart >= 0)
            {
                AddLogicHorizontalPortalSegment(logic, boundaryY, startX, segStart, endX - startX, originXcm, originZcm, side, portals);
            }
        }

        private static void AddLogicVerticalPortalSegment(
            LogicHeightmap logic,
            int boundaryX,
            int startY,
            int v0,
            int v1,
            int originXcm,
            int originZcm,
            NavPortalSide side,
            List<NavBorderPortal> portals)
        {
            GetLogicWorldXZCm(logic, boundaryX, startY + v0, out int ax, out int az);
            GetLogicWorldXZCm(logic, boundaryX, startY + v1, out int bx, out int bz);
            int len = DistanceCm(ax, az, bx, bz);
            portals.Add(new NavBorderPortal(
                side,
                side == NavPortalSide.West ? (short)0 : (short)LogicHeightmapChunk.ChunkSize,
                (short)v0,
                side == NavPortalSide.West ? (short)0 : (short)LogicHeightmapChunk.ChunkSize,
                (short)v1,
                ax - originXcm,
                az - originZcm,
                bx - originXcm,
                bz - originZcm,
                Math.Max(1, len / 2)));
        }

        private static void AddLogicHorizontalPortalSegment(
            LogicHeightmap logic,
            int boundaryY,
            int startX,
            int u0,
            int u1,
            int originXcm,
            int originZcm,
            NavPortalSide side,
            List<NavBorderPortal> portals)
        {
            GetLogicWorldXZCm(logic, startX + u0, boundaryY, out int ax, out int az);
            GetLogicWorldXZCm(logic, startX + u1, boundaryY, out int bx, out int bz);
            int len = DistanceCm(ax, az, bx, bz);
            portals.Add(new NavBorderPortal(
                side,
                (short)u0,
                side == NavPortalSide.North ? (short)0 : (short)LogicHeightmapChunk.ChunkSize,
                (short)u1,
                side == NavPortalSide.North ? (short)0 : (short)LogicHeightmapChunk.ChunkSize,
                ax - originXcm,
                az - originZcm,
                bx - originXcm,
                bz - originZcm,
                Math.Max(1, len / 2)));
        }

        private static bool IsLogicCellAnyTriangleWalkable(LogicHeightmap logic, int x, int y, in NavBuildConfig config, int layer)
        {
            if (x < 0 || y < 0 || x >= logic.WidthSamples - 1 || y >= logic.HeightSamples - 1)
            {
                return false;
            }

            if (logic.GridKind == LogicHeightmapGridKind.HexVertex)
            {
                bool isOdd = (y & 1) == 1;
                return !isOdd
                    ? IsLogicFaceWalkable(logic, x, y, x + 1, y, x, y + 1, config, layer) ||
                      IsLogicFaceWalkable(logic, x + 1, y, x + 1, y + 1, x, y + 1, config, layer)
                    : IsLogicFaceWalkable(logic, x, y, x + 1, y, x + 1, y + 1, config, layer) ||
                      IsLogicFaceWalkable(logic, x, y, x + 1, y + 1, x, y + 1, config, layer);
            }

            return IsLogicFaceWalkable(logic, x, y, x + 1, y, x, y + 1, config, layer) ||
                   IsLogicFaceWalkable(logic, x + 1, y, x + 1, y + 1, x, y + 1, config, layer);
        }

        private static bool IsLogicFaceWalkable(
            LogicHeightmap logic,
            int ax,
            int ay,
            int bx,
            int by,
            int cx,
            int cy,
            in NavBuildConfig config,
            int layer)
        {
            return TryGetLogicVertex(logic, ax, ay, out var a) &&
                   TryGetLogicVertex(logic, bx, by, out var b) &&
                   TryGetLogicVertex(logic, cx, cy, out var c) &&
                   IsLogicTriangleWalkable(a, b, c, config, layer);
        }

        private static int DistanceCm(int ax, int az, int bx, int bz)
        {
            long dx = (long)bx - ax;
            long dz = (long)bz - az;
            return (int)MathF.Round(MathF.Sqrt((float)(dx * dx + dz * dz)));
        }

        private static int ResolveLayerId(string layerId)
        {
            if (string.Equals(layerId, "Ground", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(layerId, "Water", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(layerId, "Air", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(layerId, "Mountain", StringComparison.OrdinalIgnoreCase)) return 3;
            return 0;
        }

        private static bool PolygonIntersectsTriangle(
            List<NavPointCm> poly,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            if (poly == null || poly.Count < 3)
            {
                return false;
            }

            if (PointInPolygonOrBoundary(ax, az, poly) ||
                PointInPolygonOrBoundary(bx, bz, poly) ||
                PointInPolygonOrBoundary(cx, cz, poly))
            {
                return true;
            }

            for (int i = 0; i < poly.Count; i++)
            {
                NavPointCm point = poly[i];
                if (PointInTriangle(point.Xcm, point.Zcm, ax, az, bx, bz, cx, cz))
                {
                    return true;
                }
            }

            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                int px0 = poly[j].Xcm;
                int pz0 = poly[j].Zcm;
                int px1 = poly[i].Xcm;
                int pz1 = poly[i].Zcm;
                if (SegmentsIntersect(ax, az, bx, bz, px0, pz0, px1, pz1) ||
                    SegmentsIntersect(bx, bz, cx, cz, px0, pz0, px1, pz1) ||
                    SegmentsIntersect(cx, cz, ax, az, px0, pz0, px1, pz1))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CircleIntersectsTriangle(
            int centerXcm,
            int centerZcm,
            int radiusCm,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            int radius = Math.Max(0, radiusCm);
            long radiusSquared = (long)radius * radius;
            return PointInTriangle(centerXcm, centerZcm, ax, az, bx, bz, cx, cz) ||
                DistanceSquared(centerXcm, centerZcm, ax, az) <= radiusSquared ||
                DistanceSquared(centerXcm, centerZcm, bx, bz) <= radiusSquared ||
                DistanceSquared(centerXcm, centerZcm, cx, cz) <= radiusSquared ||
                DistanceSquaredToSegment(centerXcm, centerZcm, ax, az, bx, bz) <= radiusSquared ||
                DistanceSquaredToSegment(centerXcm, centerZcm, bx, bz, cx, cz) <= radiusSquared ||
                DistanceSquaredToSegment(centerXcm, centerZcm, cx, cz, ax, az) <= radiusSquared;
        }

        private static bool PointInsideCircle(int xcm, int zcm, int centerXcm, int centerZcm, int radiusCm)
        {
            int radius = Math.Max(0, radiusCm);
            return DistanceSquared(xcm, zcm, centerXcm, centerZcm) <= (long)radius * radius;
        }

        private static bool PointInPolygonOrBoundary(int xcm, int zcm, List<NavPointCm> poly)
        {
            if (poly == null || poly.Count < 3)
            {
                return false;
            }

            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if (PointOnSegment(xcm, zcm, poly[j].Xcm, poly[j].Zcm, poly[i].Xcm, poly[i].Zcm))
                {
                    return true;
                }
            }

            return PointInPolygon(xcm, zcm, poly);
        }

        private static bool PointInPolygon(int xcm, int zcm, List<NavPointCm> poly)
        {
            if (poly == null || poly.Count < 3) return false;

            bool inside = false;
            int j = poly.Count - 1;
            for (int i = 0; i < poly.Count; j = i++)
            {
                int xi = poly[i].Xcm;
                int zi = poly[i].Zcm;
                int xj = poly[j].Xcm;
                int zj = poly[j].Zcm;

                if ((zi > zcm) == (zj > zcm)) continue;
                double xInt = (double)(xj - xi) * (zcm - zi) / (double)(zj - zi) + xi;
                if (xcm < xInt) inside = !inside;
            }

            return inside;
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

        private static bool SegmentsIntersect(
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz,
            int dx,
            int dz)
        {
            long o1 = Orient2D(ax, az, bx, bz, cx, cz);
            long o2 = Orient2D(ax, az, bx, bz, dx, dz);
            long o3 = Orient2D(cx, cz, dx, dz, ax, az);
            long o4 = Orient2D(cx, cz, dx, dz, bx, bz);

            if (o1 == 0 && PointOnSegment(cx, cz, ax, az, bx, bz)) return true;
            if (o2 == 0 && PointOnSegment(dx, dz, ax, az, bx, bz)) return true;
            if (o3 == 0 && PointOnSegment(ax, az, cx, cz, dx, dz)) return true;
            if (o4 == 0 && PointOnSegment(bx, bz, cx, cz, dx, dz)) return true;

            return (o1 > 0) != (o2 > 0) &&
                (o3 > 0) != (o4 > 0);
        }

        private static bool PointOnSegment(int px, int pz, int ax, int az, int bx, int bz)
        {
            if (Orient2D(ax, az, bx, bz, px, pz) != 0)
            {
                return false;
            }

            return px >= Math.Min(ax, bx) &&
                px <= Math.Max(ax, bx) &&
                pz >= Math.Min(az, bz) &&
                pz <= Math.Max(az, bz);
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

        private static void BuildNavTileFromDetailMesh(
            NavTile baseTile,
            int layer,
            uint tileVersion,
            ulong buildHash,
            RcPolyMeshDetail detail,
            out NavTile tile)
        {
            ResolveTileClipBounds(baseTile, out int minLocalXcm, out int maxLocalXcm, out int minLocalZcm, out int maxLocalZcm);

            var vertexIndex = new Dictionary<(int X, int Y, int Z), int>(detail.nverts);
            var vx = new List<int>(detail.nverts);
            var vy = new List<int>(detail.nverts);
            var vz = new List<int>(detail.nverts);
            var triA = new List<int>(detail.ntris);
            var triB = new List<int>(detail.ntris);
            var triC = new List<int>(detail.ntris);

            for (int m = 0; m < detail.nmeshes; m++)
            {
                int baseVert = detail.meshes[m * 4 + 0];
                int triBase = detail.meshes[m * 4 + 2];
                int triCount = detail.meshes[m * 4 + 3];

                for (int t = 0; t < triCount; t++)
                {
                    int triIndex = (triBase + t) * 4;
                    int da = detail.tris[triIndex + 0] + baseVert;
                    int db = detail.tris[triIndex + 1] + baseVert;
                    int dc = detail.tris[triIndex + 2] + baseVert;

                    var polygon = new List<DetailClipVertex>(3)
                    {
                        GetDetailLocalVertex(detail, da, baseTile),
                        GetDetailLocalVertex(detail, db, baseTile),
                        GetDetailLocalVertex(detail, dc, baseTile)
                    };

                    ClipPolygonToTileBounds(polygon, minLocalXcm, maxLocalXcm, minLocalZcm, maxLocalZcm);
                    if (polygon.Count < 3)
                    {
                        continue;
                    }

                    for (int i = 1; i < polygon.Count - 1; i++)
                    {
                        int ia = GetOrAddVertex(polygon[0], vertexIndex, vx, vy, vz);
                        int ib = GetOrAddVertex(polygon[i], vertexIndex, vx, vy, vz);
                        int ic = GetOrAddVertex(polygon[i + 1], vertexIndex, vx, vy, vz);

                        if (ia == ib || ib == ic || ia == ic) continue;
                        if (ComputeLocalArea2(vx[ia], vz[ia], vx[ib], vz[ib], vx[ic], vz[ic]) == 0) continue;

                        triA.Add(ia);
                        triB.Add(ib);
                        triC.Add(ic);
                    }
                }
            }

            var n0 = new int[triA.Count];
            var n1 = new int[triA.Count];
            var n2 = new int[triA.Count];
            Array.Fill(n0, -1);
            Array.Fill(n1, -1);
            Array.Fill(n2, -1);
            BuildAdjacency(triA, triB, triC, n0, n1, n2);
            NavBorderPortal[] portals = triA.Count > 0
                ? BuildRecastPortalsFromBasePortals(baseTile, vx, vz, triA, triB, triC)
                : Array.Empty<NavBorderPortal>();

            var tmp = new NavTile(
                new NavTileId(baseTile.TileId.ChunkX, baseTile.TileId.ChunkY, layer),
                tileVersion,
                buildHash,
                0UL,
                baseTile.OriginXcm,
                baseTile.OriginZcm,
                vx.ToArray(),
                vy.ToArray(),
                vz.ToArray(),
                triA.ToArray(),
                triB.ToArray(),
                triC.ToArray(),
                n0,
                n1,
                n2,
                portals);

            using var ms = new System.IO.MemoryStream();
            NavTileBinary.Write(ms, tmp);
            ms.Position = 0;
            tile = NavTileBinary.Read(ms);
        }

        private static NavBorderPortal[] BuildRecastPortalsFromBasePortals(
            NavTile baseTile,
            List<int> vertexXcm,
            List<int> vertexZcm,
            List<int> triA,
            List<int> triB,
            List<int> triC)
        {
            if (baseTile.Portals.Length == 0 || triA.Count == 0)
            {
                return Array.Empty<NavBorderPortal>();
            }

            var portals = new List<NavBorderPortal>(baseTile.Portals.Length);
            for (int i = 0; i < baseTile.Portals.Length; i++)
            {
                AppendPassablePortalSubsegments(
                    baseTile.Portals[i],
                    vertexXcm,
                    vertexZcm,
                    triA,
                    triB,
                    triC,
                    portals);
            }

            return portals.ToArray();
        }

        private static void AppendPassablePortalSubsegments(
            NavBorderPortal portal,
            List<int> vertexXcm,
            List<int> vertexZcm,
            List<int> triA,
            List<int> triB,
            List<int> triC,
            List<NavBorderPortal> portals)
        {
            GetPortalRawInterval(portal, out int rawStart, out int rawEnd);
            int rawSpan = rawEnd - rawStart;
            if (rawSpan == 0)
            {
                return;
            }

            int lengthCm = DistanceCm(portal.LeftXcm, portal.LeftZcm, portal.RightXcm, portal.RightZcm);
            int divisions = Math.Clamp(Math.Max(Math.Abs(rawSpan) * 4, lengthCm / 200), 8, 512);
            int projectionToleranceCm = Math.Clamp(lengthCm / 64, 64, 1024);
            int runStart = -1;
            for (int i = 0; i < divisions; i++)
            {
                double t = (i + 0.5d) / divisions;
                int sampleX = (int)Math.Round(portal.LeftXcm + ((portal.RightXcm - portal.LeftXcm) * t));
                int sampleZ = (int)Math.Round(portal.LeftZcm + ((portal.RightZcm - portal.LeftZcm) * t));
                bool passable = IsPortalSampleWalkable(
                    vertexXcm,
                    vertexZcm,
                    triA,
                    triB,
                    triC,
                    portal.Side,
                    sampleX,
                    sampleZ,
                    projectionToleranceCm);

                if (passable)
                {
                    if (runStart < 0)
                    {
                        runStart = i;
                    }
                }
                else if (runStart >= 0)
                {
                    AddPortalRun(portal, rawStart, rawEnd, runStart, i, divisions, portals);
                    runStart = -1;
                }
            }

            if (runStart >= 0)
            {
                AddPortalRun(portal, rawStart, rawEnd, runStart, divisions, divisions, portals);
            }
        }

        private static bool IsPortalSampleWalkable(
            List<int> vertexXcm,
            List<int> vertexZcm,
            List<int> triA,
            List<int> triB,
            List<int> triC,
            NavPortalSide side,
            int sampleX,
            int sampleZ,
            int projectionToleranceCm)
        {
            if (PointInsideAnyTriangle(vertexXcm, vertexZcm, triA, triB, triC, sampleX, sampleZ))
            {
                return true;
            }

            (int ox, int oz) = side switch
            {
                NavPortalSide.West => (8, 0),
                NavPortalSide.East => (-8, 0),
                NavPortalSide.North => (0, 8),
                NavPortalSide.South => (0, -8),
                _ => (0, 0)
            };

            if ((ox != 0 || oz != 0) &&
                PointInsideAnyTriangle(vertexXcm, vertexZcm, triA, triB, triC, sampleX + ox, sampleZ + oz))
            {
                return true;
            }

            return PointNearAnyTriangle(
                vertexXcm,
                vertexZcm,
                triA,
                triB,
                triC,
                sampleX + ox,
                sampleZ + oz,
                projectionToleranceCm);
        }

        private static bool PointInsideAnyTriangle(
            List<int> vertexXcm,
            List<int> vertexZcm,
            List<int> triA,
            List<int> triB,
            List<int> triC,
            int xcm,
            int zcm)
        {
            for (int i = 0; i < triA.Count; i++)
            {
                int a = triA[i];
                int b = triB[i];
                int c = triC[i];
                if (PointInTriangleInclusive(
                        xcm,
                        zcm,
                        vertexXcm[a],
                        vertexZcm[a],
                        vertexXcm[b],
                        vertexZcm[b],
                        vertexXcm[c],
                        vertexZcm[c]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PointNearAnyTriangle(
            List<int> vertexXcm,
            List<int> vertexZcm,
            List<int> triA,
            List<int> triB,
            List<int> triC,
            int xcm,
            int zcm,
            int maxDistanceCm)
        {
            long maxD2 = (long)Math.Max(0, maxDistanceCm) * Math.Max(0, maxDistanceCm);
            for (int i = 0; i < triA.Count; i++)
            {
                int a = triA[i];
                int b = triB[i];
                int c = triC[i];
                if (DistanceSquaredToTriangle2D(
                        xcm,
                        zcm,
                        vertexXcm[a],
                        vertexZcm[a],
                        vertexXcm[b],
                        vertexZcm[b],
                        vertexXcm[c],
                        vertexZcm[c]) <= maxD2)
                {
                    return true;
                }
            }

            return false;
        }

        private static long DistanceSquaredToTriangle2D(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            if (PointInTriangleInclusive(px, pz, ax, az, bx, bz, cx, cz))
            {
                return 0;
            }

            long ab = DistanceSquaredToSegment(px, pz, ax, az, bx, bz);
            long bc = DistanceSquaredToSegment(px, pz, bx, bz, cx, cz);
            long ca = DistanceSquaredToSegment(px, pz, cx, cz, ax, az);
            return Math.Min(ab, Math.Min(bc, ca));
        }

        private static bool PointInTriangleInclusive(
            int px,
            int pz,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            long d0 = Orient2D(ax, az, bx, bz, px, pz);
            long d1 = Orient2D(bx, bz, cx, cz, px, pz);
            long d2 = Orient2D(cx, cz, ax, az, px, pz);
            bool hasNegative = d0 < 0 || d1 < 0 || d2 < 0;
            bool hasPositive = d0 > 0 || d1 > 0 || d2 > 0;
            return !(hasNegative && hasPositive);
        }

        private static void AddPortalRun(
            NavBorderPortal portal,
            int rawStart,
            int rawEnd,
            int runStart,
            int runEnd,
            int divisions,
            List<NavBorderPortal> portals)
        {
            if (runEnd <= runStart)
            {
                return;
            }

            double t0 = runStart / (double)divisions;
            double t1 = runEnd / (double)divisions;
            int i0 = InterpolatePortalInterval(rawStart, rawEnd, t0, towardStart: true);
            int i1 = InterpolatePortalInterval(rawStart, rawEnd, t1, towardStart: false);
            if (i0 == i1)
            {
                if (rawEnd > rawStart)
                {
                    i1 = Math.Min(rawEnd, i0 + 1);
                }
                else
                {
                    i1 = Math.Max(rawEnd, i0 - 1);
                }
            }

            int ax = InterpolateInt(portal.LeftXcm, portal.RightXcm, rawStart, rawEnd, i0);
            int az = InterpolateInt(portal.LeftZcm, portal.RightZcm, rawStart, rawEnd, i0);
            int bx = InterpolateInt(portal.LeftXcm, portal.RightXcm, rawStart, rawEnd, i1);
            int bz = InterpolateInt(portal.LeftZcm, portal.RightZcm, rawStart, rawEnd, i1);
            int lengthCm = DistanceCm(ax, az, bx, bz);
            if (lengthCm < 8)
            {
                return;
            }

            short su0 = portal.U0;
            short sv0 = portal.V0;
            short su1 = portal.U1;
            short sv1 = portal.V1;
            if (portal.Side == NavPortalSide.West || portal.Side == NavPortalSide.East)
            {
                sv0 = ToShortClamped(i0);
                sv1 = ToShortClamped(i1);
            }
            else
            {
                su0 = ToShortClamped(i0);
                su1 = ToShortClamped(i1);
            }

            portals.Add(new NavBorderPortal(
                portal.Side,
                su0,
                sv0,
                su1,
                sv1,
                ax,
                az,
                bx,
                bz,
                Math.Max(1, lengthCm / 2)));
        }

        private static int InterpolatePortalInterval(int rawStart, int rawEnd, double t, bool towardStart)
        {
            double value = rawStart + ((rawEnd - rawStart) * t);
            if (rawEnd >= rawStart)
            {
                return towardStart ? (int)Math.Floor(value) : (int)Math.Ceiling(value);
            }

            return towardStart ? (int)Math.Ceiling(value) : (int)Math.Floor(value);
        }

        private static short ToShortClamped(int value)
        {
            return (short)Math.Clamp(value, short.MinValue, short.MaxValue);
        }

        private static void GetPortalRawInterval(NavBorderPortal portal, out int start, out int end)
        {
            if (portal.Side == NavPortalSide.West || portal.Side == NavPortalSide.East)
            {
                start = portal.V0;
                end = portal.V1;
                return;
            }

            start = portal.U0;
            end = portal.U1;
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

        private readonly struct DetailClipVertex
        {
            public readonly double Xcm;
            public readonly double Ycm;
            public readonly double Zcm;

            public DetailClipVertex(double xcm, double ycm, double zcm)
            {
                Xcm = xcm;
                Ycm = ycm;
                Zcm = zcm;
            }
        }

        private static void ResolveTileClipBounds(
            NavTile baseTile,
            out int minLocalXcm,
            out int maxLocalXcm,
            out int minLocalZcm,
            out int maxLocalZcm)
        {
            minLocalXcm = 0;
            maxLocalXcm = 0;
            minLocalZcm = 0;
            maxLocalZcm = 0;

            if (baseTile.VertexCount == 0)
            {
                return;
            }

            minLocalXcm = baseTile.VertexXcm[0];
            maxLocalXcm = baseTile.VertexXcm[0];
            minLocalZcm = baseTile.VertexZcm[0];
            maxLocalZcm = baseTile.VertexZcm[0];
            for (int i = 1; i < baseTile.VertexCount; i++)
            {
                minLocalXcm = Math.Min(minLocalXcm, baseTile.VertexXcm[i]);
                maxLocalXcm = Math.Max(maxLocalXcm, baseTile.VertexXcm[i]);
                minLocalZcm = Math.Min(minLocalZcm, baseTile.VertexZcm[i]);
                maxLocalZcm = Math.Max(maxLocalZcm, baseTile.VertexZcm[i]);
            }
        }

        private static DetailClipVertex GetDetailLocalVertex(
            RcPolyMeshDetail detail,
            int detailVertexIndex,
            NavTile baseTile)
        {
            int vi = detailVertexIndex * 3;
            double worldXcm = detail.verts[vi + 0] * 100.0;
            double worldYcm = detail.verts[vi + 1] * 100.0;
            double worldZcm = detail.verts[vi + 2] * 100.0;
            return new DetailClipVertex(worldXcm - baseTile.OriginXcm, worldYcm, worldZcm - baseTile.OriginZcm);
        }

        private static int GetOrAddVertex(
            DetailClipVertex vertex,
            Dictionary<(int X, int Y, int Z), int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz)
        {
            int localXcm = (int)Math.Round(vertex.Xcm);
            int worldYcm = (int)Math.Round(vertex.Ycm);
            int localZcm = (int)Math.Round(vertex.Zcm);

            var key = (localXcm, worldYcm, localZcm);
            if (vertexIndex.TryGetValue(key, out int existing)) return existing;

            int id = vx.Count;
            vx.Add(localXcm);
            vy.Add(worldYcm);
            vz.Add(localZcm);
            vertexIndex[key] = id;
            return id;
        }

        private static void ClipPolygonToTileBounds(
            List<DetailClipVertex> polygon,
            int minLocalXcm,
            int maxLocalXcm,
            int minLocalZcm,
            int maxLocalZcm)
        {
            ClipPolygon(polygon, minLocalXcm, axis: 0, keepGreater: true);
            ClipPolygon(polygon, maxLocalXcm, axis: 0, keepGreater: false);
            ClipPolygon(polygon, minLocalZcm, axis: 1, keepGreater: true);
            ClipPolygon(polygon, maxLocalZcm, axis: 1, keepGreater: false);
        }

        private static void ClipPolygon(
            List<DetailClipVertex> polygon,
            int boundary,
            int axis,
            bool keepGreater)
        {
            if (polygon.Count == 0)
            {
                return;
            }

            var input = new List<DetailClipVertex>(polygon);
            polygon.Clear();
            DetailClipVertex previous = input[input.Count - 1];
            bool previousInside = IsClipInside(previous, boundary, axis, keepGreater);

            for (int i = 0; i < input.Count; i++)
            {
                DetailClipVertex current = input[i];
                bool currentInside = IsClipInside(current, boundary, axis, keepGreater);

                if (currentInside)
                {
                    if (!previousInside)
                    {
                        polygon.Add(InterpolateClipVertex(previous, current, boundary, axis));
                    }

                    polygon.Add(current);
                }
                else if (previousInside)
                {
                    polygon.Add(InterpolateClipVertex(previous, current, boundary, axis));
                }

                previous = current;
                previousInside = currentInside;
            }
        }

        private static bool IsClipInside(DetailClipVertex vertex, int boundary, int axis, bool keepGreater)
        {
            double value = axis == 0 ? vertex.Xcm : vertex.Zcm;
            return keepGreater ? value >= boundary : value <= boundary;
        }

        private static DetailClipVertex InterpolateClipVertex(DetailClipVertex a, DetailClipVertex b, int boundary, int axis)
        {
            double av = axis == 0 ? a.Xcm : a.Zcm;
            double bv = axis == 0 ? b.Xcm : b.Zcm;
            if (Math.Abs(bv - av) <= 1e-9)
            {
                return a;
            }

            double t = (boundary - av) / (bv - av);
            return new DetailClipVertex(
                a.Xcm + ((b.Xcm - a.Xcm) * t),
                a.Ycm + ((b.Ycm - a.Ycm) * t),
                a.Zcm + ((b.Zcm - a.Zcm) * t));
        }

        private static long ComputeLocalArea2(int ax, int az, int bx, int bz, int cx, int cz)
        {
            return (((long)bx - ax) * ((long)cz - az)) - (((long)bz - az) * ((long)cx - ax));
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly int A;
            public readonly int B;

            public EdgeKey(int a, int b)
            {
                if (a <= b)
                {
                    A = a;
                    B = b;
                }
                else
                {
                    A = b;
                    B = a;
                }
            }

            public bool Equals(EdgeKey other) => A == other.A && B == other.B;
            public override bool Equals(object? obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(A, B);
        }

        private static void BuildAdjacency(List<int> triA, List<int> triB, List<int> triC, int[] n0, int[] n1, int[] n2)
        {
            var map = new Dictionary<EdgeKey, (int tri, int edge)>(triA.Count * 3);
            for (int i = 0; i < triA.Count; i++)
            {
                AddEdge(i, 0, triA[i], triB[i]);
                AddEdge(i, 1, triB[i], triC[i]);
                AddEdge(i, 2, triC[i], triA[i]);
            }

            void AddEdge(int tri, int edge, int va, int vb)
            {
                var key = new EdgeKey(va, vb);
                if (!map.TryGetValue(key, out var other))
                {
                    map[key] = (tri, edge);
                    return;
                }

                SetNeighbor(tri, edge, other.tri);
                SetNeighbor(other.tri, other.edge, tri);
            }

            void SetNeighbor(int tri, int edge, int neighbor)
            {
                if (edge == 0) n0[tri] = neighbor;
                else if (edge == 1) n1[tri] = neighbor;
                else n2[tri] = neighbor;
            }
        }
    }
}
