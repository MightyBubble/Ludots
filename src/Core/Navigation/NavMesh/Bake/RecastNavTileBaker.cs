using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Spatial;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;

using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public sealed class RecastNavBakeAlgorithm : INavBakeAlgorithm
    {
        public NavBakeAlgorithmKind Kind => NavBakeAlgorithmKind.Recast;

        public bool TryBake(
            NavBakeContext context,
            NavBakeTileCoord target,
            NavLayerConfig layer,
            NavMeshAgentProfileConfig navProfile,
            AgentProfileConfig agentProfile,
            out NavTile tile,
            out byte[] detourTileBytes,
            out NavBakeArtifact artifact)
        {
            NavTerrainFeedKind feed = context.Config != null
                ? context.Config.ParsedTerrainFeed
                : NavTerrainFeedKind.Triangles;
            return RecastNavTileBaker.TryBake(
                context.Terrain,
                target.ChunkX,
                target.ChunkY,
                context.TileVersion,
                context.BuildConfig,
                agentProfile,
                navProfile,
                layer.Layer,
                layer.Id,
                context.Obstacles,
                out tile,
                out detourTileBytes,
                out artifact,
                feed);
        }
    }

    public static class RecastNavTileBaker
    {
        private const float CmPerMeter = SpatialScaleDefaults.CellCm;

        /// <summary>
        /// Hard cap on the solid heightfield column count. RcHeightfield allocates one
        /// span reference per column, so the column count must stay far below the int32
        /// product wrap-around; tiles above the cap are a configuration error, not a bake.
        /// </summary>
        public const long MaxSolidVoxelColumns = 33_554_432;

        /// <summary>Detour area id 63 doubles as Recast's walkable marker inside the direct
        /// terrain feed; authored area ids must stay below it so the marker stays unambiguous.</summary>
        public const int ReservedWalkableAreaId = RcRecast.RC_WALKABLE_AREA;

        public static bool TryBake(
            VertexMap map,
            int chunkX,
            int chunkY,
            uint tileVersion,
            in NavBuildConfig legacyConfig,
            AgentProfileConfig agentProfile,
            NavMeshAgentProfileConfig navProfile,
            int layer,
            string layerId,
            NavObstacleSet obstacles,
            out NavTile tile,
            out byte[] detourTileBytes,
            out NavBakeArtifact artifact)
        {
            detourTileBytes = Array.Empty<byte>();
            if (map == null)
            {
                tile = null!;
                artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, layer), tileVersion, NavBakeStage.None, NavBakeErrorCode.InvalidInput, "VertexMap is null.", 0, 0, 0, 0);
                return false;
            }

            return TryBake(new VertexMapLogicTerrainField(map), chunkX, chunkY, tileVersion, legacyConfig, agentProfile, navProfile, layer, layerId, obstacles, out tile, out detourTileBytes, out artifact);
        }

        public static bool TryBake(
            LogicTerrainField terrain,
            int chunkX,
            int chunkY,
            uint tileVersion,
            in NavBuildConfig legacyConfig,
            AgentProfileConfig agentProfile,
            NavMeshAgentProfileConfig navProfile,
            int layer,
            string layerId,
            NavObstacleSet obstacles,
            out NavTile tile,
            out byte[] detourTileBytes,
            out NavBakeArtifact artifact,
            NavTerrainFeedKind terrainFeed = NavTerrainFeedKind.Triangles)
        {
            tile = null!;
            detourTileBytes = Array.Empty<byte>();
            artifact = default;

            if (!NavTileBuilder.TryBuildTile(terrain, chunkX, chunkY, tileVersion, legacyConfig, out var baseTile, out var baseArtifact))
            {
                artifact = baseArtifact;
                return false;
            }

            try
            {
                ComputeTileFootprintBounds(terrain, chunkX, chunkY, out float tileMinX, out float tileMinZ, out float tileMaxX, out float tileMaxZ);
                var rcCfg = BuildRcConfig(terrain, agentProfile, navProfile, tileMinX, tileMinZ, tileMaxX, tileMaxZ);

                long widthVoxels = (long)rcCfg.TileSizeX + 2L * rcCfg.BorderSize;
                long heightVoxels = (long)rcCfg.TileSizeZ + 2L * rcCfg.BorderSize;
                long solidColumns = widthVoxels * heightVoxels;
                if (solidColumns > MaxSolidVoxelColumns)
                {
                    artifact = new NavBakeArtifact(
                        new NavTileId(chunkX, chunkY, layer),
                        tileVersion,
                        NavBakeStage.WalkMask,
                        NavBakeErrorCode.VoxelBudgetExceeded,
                        $"Tile solid heightfield needs {solidColumns:N0} voxel columns (tile {tileMaxX - tileMinX:F0}m × {tileMaxZ - tileMinZ:F0}m at cs={rcCfg.Cs}m); cap is {MaxSolidVoxelColumns:N0}. Coarsen the cell size or bake smaller tiles.",
                        0, 0, 0, 0);
                    return false;
                }

                RcVec3f tileBmin;
                RcVec3f tileBmax;
                RcBuilderResult rcResult;
                bool areasFromPolymesh;
                if (terrainFeed == NavTerrainFeedKind.Direct)
                {
                    RcHeightfield? solid = RecastDirectFeedHeightfield.BuildSolidHeightfield(
                        terrain, chunkX, chunkY, legacyConfig, rcCfg,
                        tileMinX, tileMinZ, tileMaxX, tileMaxZ, obstacles, layerId);
                    if (solid == null)
                    {
                        artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, layer), tileVersion, NavBakeStage.WalkMask, NavBakeErrorCode.NoWalkableDomain, "No walkable columns after direct heightfield feed.", 0, 0, 0, 0);
                        return false;
                    }

                    tileBmin = new RcVec3f(tileMinX, solid.bmin.Y, tileMinZ);
                    tileBmax = new RcVec3f(
                        tileMinX + rcCfg.TileSizeX * rcCfg.Cs,
                        solid.bmax.Y,
                        tileMinZ + rcCfg.TileSizeZ * rcCfg.Cs);
                    rcResult = new RcBuilder().Build(new RcContext(), tileX: 0, tileZ: 0, geom: null!, rcCfg, solid, keepInterResults: false);
                    areasFromPolymesh = true;
                }
                else
                {
                    BuildExpandedRecastTriangleMesh(terrain, chunkX, chunkY, tileVersion, legacyConfig, rcCfg, obstacles, layerId, out var verts, out var tris);
                    if (tris.Count == 0)
                    {
                        artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, layer), tileVersion, NavBakeStage.Triangulate, NavBakeErrorCode.NoWalkableDomain, "No triangles after obstacle filtering.", 0, 0, 0, 0);
                        return false;
                    }

                    var geom = new RcSampleInputGeomProvider(verts.ToArray(), tris.ToArray());
                    RcVec3f geomMin = geom.GetMeshBoundsMin();
                    RcVec3f geomMax = geom.GetMeshBoundsMax();
                    tileBmin = new RcVec3f(tileMinX, geomMin.Y, tileMinZ);
                    tileBmax = new RcVec3f(
                        tileMinX + rcCfg.TileSizeX * rcCfg.Cs,
                        geomMax.Y,
                        tileMinZ + rcCfg.TileSizeZ * rcCfg.Cs);
                    var bcfg = new RcBuilderConfig(rcCfg, tileBmin, tileBmax, tileX: 0, tileZ: 0);
                    rcResult = new RcBuilder().Build(geom, bcfg, keepInterResults: false);
                    areasFromPolymesh = false;
                }

                if (rcResult?.Mesh == null || rcResult.MeshDetail == null || rcResult.MeshDetail.ntris <= 0)
                {
                    // Strategy-resolution boards feed Recast one triangle per logic cell. When
                    // DotRecast drops detail (tiny island / single-cell land), the LogicTerrain
                    // mesh from NavTileBuilder is already the authoritative walkable domain —
                    // publish it instead of failing the whole offline bake.
                    if (baseTile.TriangleCount > 0 && baseArtifact.WalkableTriangleCount > 0)
                    {
                        tile = baseTile.TileId.Layer == layer
                            ? baseTile
                            : NavTileLayerRewriter.WithLayer(baseTile, layer);
                        detourTileBytes = Array.Empty<byte>();
                        artifact = new NavBakeArtifact(
                            tile.TileId,
                            tile.TileVersion,
                            NavBakeStage.Serialize,
                            NavBakeErrorCode.None,
                            "Recast detail empty; published LogicTerrain strategy mesh.",
                            baseArtifact.WalkableTriangleCount,
                            tile.VertexCount,
                            tile.TriangleCount,
                            tile.Portals.Length);
                        return true;
                    }

                    artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, layer), tileVersion, NavBakeStage.Triangulate, NavBakeErrorCode.TriangulationFailed, "Recast produced empty detail mesh.", 0, 0, 0, 0);
                    return false;
                }

                PrepareDetourPolygons(baseTile, rcResult.Mesh, resolveAreasFromBaseTile: !areasFromPolymesh);

                BuildNavTileFromDetailMesh(
                    baseTile,
                    layer,
                    tileVersion,
                    legacyConfig.ComputeHash() ^ ((ulong)(byte)terrainFeed << 56),
                    rcResult.MeshDetail,
                    rcResult.Mesh,
                    areaFromPolymesh: areasFromPolymesh,
                    out tile);

                detourTileBytes = BuildDetourTileBytes(
                    rcResult,
                    agentProfile,
                    navProfile,
                    chunkX,
                    chunkY,
                    layer,
                    tileBmin,
                    tileBmax);

                artifact = new NavBakeArtifact(tile.TileId, tile.TileVersion, NavBakeStage.Serialize, NavBakeErrorCode.None, "", baseArtifact.WalkableTriangleCount, tile.VertexCount, tile.TriangleCount, tile.Portals.Length);
                return true;
            }
            catch (Exception ex)
            {
                artifact = new NavBakeArtifact(new NavTileId(chunkX, chunkY, layer), tileVersion, NavBakeStage.Serialize, NavBakeErrorCode.SerializationFailed, ex.Message, 0, 0, 0, 0);
                tile = null!;
                detourTileBytes = Array.Empty<byte>();
                return false;
            }
        }

        private static void PrepareDetourPolygons(NavTile baseTile, RcPolyMesh mesh, bool resolveAreasFromBaseTile)
        {
            if (mesh.flags == null || mesh.flags.Length < mesh.npolys)
            {
                mesh.flags = new int[mesh.npolys];
            }

            for (int i = 0; i < mesh.npolys; i++)
            {
                mesh.flags[i] = 1;
                // Direct feed carries Recast's own walkable marker; the tile vocabulary
                // uses 0 for default walkable, matching the triangle path's resolution.
                byte areaId = resolveAreasFromBaseTile
                    ? ResolveAreaIdFromPolyMesh(baseTile, mesh, i)
                    : (mesh.areas[i] == RcRecast.RC_WALKABLE_AREA ? (byte)0 : (byte)mesh.areas[i]);
                if (areaId >= DtDetour.DT_MAX_AREAS)
                {
                    throw new InvalidOperationException(
                        $"NavTile {baseTile.TileId} polygon {i} area id {areaId} exceeds Detour max area id {DtDetour.DT_MAX_AREAS - 1}.");
                }

                mesh.areas[i] = areaId;
            }

            MarkDetourTilePortals(mesh);
        }

        private static byte ResolveAreaIdFromPolyMesh(NavTile baseTile, RcPolyMesh mesh, int polyIndex)
        {
            int p = polyIndex * mesh.nvp * 2;
            float sx = 0f;
            float sz = 0f;
            int count = 0;
            for (int j = 0; j < mesh.nvp; j++)
            {
                int vi = mesh.polys[p + j];
                if (vi == RcRecast.RC_MESH_NULL_IDX) break;
                int v = vi * 3;
                sx += mesh.bmin.X + mesh.verts[v + 0] * mesh.cs;
                sz += mesh.bmin.Z + mesh.verts[v + 2] * mesh.cs;
                count++;
            }

            if (count == 0)
            {
                return 0;
            }

            int localXcm = (int)MathF.Round((sx / count) * CmPerMeter) - baseTile.OriginXcm;
            int localZcm = (int)MathF.Round((sz / count) * CmPerMeter) - baseTile.OriginZcm;

            for (int i = 0; i < baseTile.TriangleCount; i++)
            {
                int ia = baseTile.TriA[i];
                int ib = baseTile.TriB[i];
                int ic = baseTile.TriC[i];
                if (PointInTriangle2D(
                    localXcm,
                    localZcm,
                    baseTile.VertexXcm[ia],
                    baseTile.VertexZcm[ia],
                    baseTile.VertexXcm[ib],
                    baseTile.VertexZcm[ib],
                    baseTile.VertexXcm[ic],
                    baseTile.VertexZcm[ic]))
                {
                    return baseTile.TriAreaIds[i];
                }
            }

            return 0;
        }

        private static void MarkDetourTilePortals(RcPolyMesh mesh)
        {
            int maxX = Math.Max(0, (int)MathF.Round((mesh.bmax.X - mesh.bmin.X) / mesh.cs));
            int maxZ = Math.Max(0, (int)MathF.Round((mesh.bmax.Z - mesh.bmin.Z) / mesh.cs));
            for (int i = 0; i < mesh.npolys; i++)
            {
                int p = i * mesh.nvp * 2;
                for (int j = 0; j < mesh.nvp; j++)
                {
                    int va = mesh.polys[p + j];
                    if (va == RcRecast.RC_MESH_NULL_IDX) break;
                    if (mesh.polys[p + mesh.nvp + j] != RcRecast.RC_MESH_NULL_IDX) continue;
                    int next = j + 1;
                    if (next >= mesh.nvp || mesh.polys[p + next] == RcRecast.RC_MESH_NULL_IDX) next = 0;
                    int vb = mesh.polys[p + next];
                    if (vb == RcRecast.RC_MESH_NULL_IDX) continue;

                    int a = va * 3;
                    int b = vb * 3;
                    int ax = mesh.verts[a + 0];
                    int az = mesh.verts[a + 2];
                    int bx = mesh.verts[b + 0];
                    int bz = mesh.verts[b + 2];

                    if (Near(ax, 0) && Near(bx, 0)) mesh.polys[p + mesh.nvp + j] = 0x8000 | 0;
                    else if (Near(az, maxZ) && Near(bz, maxZ)) mesh.polys[p + mesh.nvp + j] = 0x8000 | 1;
                    else if (Near(ax, maxX) && Near(bx, maxX)) mesh.polys[p + mesh.nvp + j] = 0x8000 | 2;
                    else if (Near(az, 0) && Near(bz, 0)) mesh.polys[p + mesh.nvp + j] = 0x8000 | 3;
                }
            }
        }

        private static bool Near(int value, int expected)
            => Math.Abs(value - expected) <= 1;

        private static byte[] BuildDetourTileBytes(
            RcBuilderResult rcResult,
            AgentProfileConfig agentProfile,
            NavMeshAgentProfileConfig navProfile,
            int chunkX,
            int chunkY,
            int layer,
            RcVec3f tileBmin,
            RcVec3f tileBmax)
        {
            RcPolyMesh pmesh = rcResult.Mesh;
            RcPolyMeshDetail dmesh = rcResult.MeshDetail;

            var option = new DtNavMeshCreateParams
            {
                verts = pmesh.verts,
                vertCount = pmesh.nverts,
                polys = pmesh.polys,
                polyAreas = pmesh.areas,
                polyFlags = pmesh.flags,
                polyCount = pmesh.npolys,
                nvp = pmesh.nvp,
                detailMeshes = dmesh.meshes,
                detailVerts = dmesh.verts,
                detailVertsCount = dmesh.nverts,
                detailTris = dmesh.tris,
                detailTriCount = dmesh.ntris,
                walkableHeight = agentProfile.HeightCm / CmPerMeter,
                walkableRadius = agentProfile.RadiusCm / CmPerMeter,
                walkableClimb = navProfile.MaxClimbCm / CmPerMeter,
                bmin = new RcVec3f(tileBmin.X, pmesh.bmin.Y, tileBmin.Z),
                bmax = new RcVec3f(tileBmax.X, pmesh.bmax.Y, tileBmax.Z),
                cs = pmesh.cs,
                ch = pmesh.ch,
                tileX = chunkX,
                tileZ = chunkY,
                tileLayer = layer,
                buildBvTree = true
            };

            DtMeshData data = DtNavMeshBuilder.CreateNavMeshData(option)
                ?? throw new InvalidOperationException($"DotRecast failed to create Detour tile data for chunk ({chunkX},{chunkY}) layer {layer}.");

            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            new DtMeshDataWriter().Write(writer, data, RcByteOrder.LITTLE_ENDIAN, cCompatibility: false);
            writer.Flush();
            return ms.ToArray();
        }

        internal static void ComputeTileFootprintBounds(
            LogicTerrainField terrain,
            int chunkX,
            int chunkY,
            out float minX,
            out float minZ,
            out float maxX,
            out float maxZ)
        {
            int startC = chunkX * terrain.ChunkSizeCells;
            int startR = chunkY * terrain.ChunkSizeCells;
            int endC = startC + terrain.TileWidthCells(chunkX);
            int endR = startR + terrain.TileHeightCells(chunkY);

            float localMinX = float.PositiveInfinity;
            float localMinZ = float.PositiveInfinity;
            float localMaxX = float.NegativeInfinity;
            float localMaxZ = float.NegativeInfinity;

            for (int r = startR; r <= endR; r++)
            {
                Include(startC, r);
                Include(endC, r);
            }

            for (int c = startC; c <= endC; c++)
            {
                Include(c, startR);
                Include(c, endR);
            }

            void Include(int c, int r)
            {
                terrain.GetWorldPositionMeters(c, r, out float x, out float z);
                localMinX = MathF.Min(localMinX, x);
                localMinZ = MathF.Min(localMinZ, z);
                localMaxX = MathF.Max(localMaxX, x);
                localMaxZ = MathF.Max(localMaxZ, z);
            }

            minX = localMinX;
            minZ = localMinZ;
            maxX = localMaxX;
            maxZ = localMaxZ;
        }

        // Agent-radius voxel clamp stays for tactical maps. Continental / strategy
        // boards raise LogicTerrain cell size above that clamp; Recast must follow the
        // coarser terrain step or a single tile allocates hundreds of thousands of columns.
        internal const int MaxRecastVoxelsPerAxis = 512;

        private static RcConfig BuildRcConfig(
            LogicTerrainField terrain,
            AgentProfileConfig agentProfile,
            NavMeshAgentProfileConfig navProfile,
            float tileMinX,
            float tileMinZ,
            float tileMaxX,
            float tileMaxZ)
        {
            float radius = agentProfile.RadiusCm / CmPerMeter;
            float height = agentProfile.HeightCm / CmPerMeter;
            float maxClimb = navProfile.MaxClimbCm / CmPerMeter;
            float maxSlope = navProfile.MaxSlopeDeg;

            float agentCellSize = MathF.Max(0.05f, MathF.Min(0.5f, radius / 3f));
            float terrainCellSize = GetTerrainCellStepMeters(terrain);
            float cellSize = MathF.Max(agentCellSize, terrainCellSize);
            float cellHeight = MathF.Max(cellSize * 0.5f, MathF.Max(0.01f, maxClimb));
            int tileSizeX = Math.Max(1, (int)MathF.Ceiling((tileMaxX - tileMinX) / cellSize));
            int tileSizeZ = Math.Max(1, (int)MathF.Ceiling((tileMaxZ - tileMinZ) / cellSize));
            if (tileSizeX > MaxRecastVoxelsPerAxis || tileSizeZ > MaxRecastVoxelsPerAxis)
            {
                throw new InvalidOperationException(
                    $"Recast voxel grid {tileSizeX}x{tileSizeZ} exceeds {MaxRecastVoxelsPerAxis} per axis " +
                    $"(cellSize={cellSize:R}m, tile=[{tileMinX:R},{tileMinZ:R}]-[{tileMaxX:R},{tileMaxZ:R}]). " +
                    "Raise LogicTerrain cell size or shrink the tile footprint; refusing to allocate.");
            }

            int borderSize = RcConfig.CalcBorder(radius, cellSize);

            // detail 采样参数是运行时重烤的稳定性约束（NAV-R2）：采样间距过细 +
            // 误差阈值过紧时，BuildPolyDetail 的逐样例插入循环在大多边形上呈平方级
            // 膨胀，量化地形上误差永难收敛 → 单瓦分钟级阻塞。hull 顶点高度本就精确，
            // 粗间距 + 宽误差只损失面内高度细化，不影响寻路拓扑。sampleDist=0 会令
            // 部分多边形 detail 为空、Detour 序列化越界，故必须保持非零。
            // Continental strategy cells are kilometers across; keep agent radius as-is so
            // Recast does not erode an entire logic cell away from small landmasses.
            float detailSampleDist = MathF.Max(16f, cellSize);
            float detailSampleMaxError = MathF.Max(4f, cellSize * 0.25f);
            return new RcConfig(
                true,
                tileSizeX,
                tileSizeZ,
                borderSize,
                DotRecast.Recast.RcPartition.WATERSHED,
                cellSize, cellHeight,
                maxSlope, height, radius, maxClimb,
                8 * 8 * cellSize * cellSize,
                20 * 20 * cellSize * cellSize,
                12f, 1.3f,
                6,
                detailSampleDist, detailSampleMaxError,
                true, true, true,
                new RcAreaModification(RcRecast.RC_WALKABLE_AREA), true);
        }

        private static void BuildExpandedRecastTriangleMesh(
            LogicTerrainField terrain,
            int chunkX,
            int chunkY,
            uint tileVersion,
            in NavBuildConfig legacyConfig,
            RcConfig rcCfg,
            NavObstacleSet obstacles,
            string layerId,
            out List<float> verts,
            out List<int> tris)
        {
            int expandedCells = Math.Max(1, (int)MathF.Ceiling(rcCfg.BorderSize * rcCfg.Cs / Math.Max(0.01f, GetTerrainCellStepMeters(terrain))));
            int expandedChunkRadius = Math.Max(1, (expandedCells + terrain.ChunkSizeCells - 1) / terrain.ChunkSizeCells);
            int minChunkX = Math.Max(0, chunkX - expandedChunkRadius);
            int minChunkY = Math.Max(0, chunkY - expandedChunkRadius);
            int maxChunkX = Math.Min(terrain.WidthChunks - 1, chunkX + expandedChunkRadius);
            int maxChunkY = Math.Min(terrain.HeightChunks - 1, chunkY + expandedChunkRadius);

            verts = new List<float>();
            tris = new List<int>();

            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                for (int x = minChunkX; x <= maxChunkX; x++)
                {
                    if (!NavTileBuilder.TryBuildTile(terrain, x, y, tileVersion, legacyConfig, out var tile, out var artifact))
                    {
                        if (x == chunkX && y == chunkY)
                        {
                            throw new InvalidOperationException($"Target tile failed during expanded Recast input build: {artifact.Message}");
                        }

                        continue;
                    }

                    AppendRecastTriangleMesh(tile, obstacles, layerId, verts, tris);
                }
            }
        }

        private static float GetTerrainCellStepMeters(LogicTerrainField terrain)
        {
            int stepCm = Math.Max(1, Math.Min(terrain.HorizontalStepCm, terrain.VerticalStepCm));
            return stepCm / CmPerMeter;
        }

        private static void AppendRecastTriangleMesh(NavTile baseTile, NavObstacleSet obstacles, string layerId, List<float> verts, List<int> tris)
        {
            int vertexBase = verts.Count / 3;
            int vCount = baseTile.VertexCount;
            for (int i = 0; i < vCount; i++)
            {
                verts.Add((baseTile.OriginXcm + baseTile.VertexXcm[i]) / CmPerMeter);
                verts.Add(baseTile.VertexYcm[i] / CmPerMeter);
                verts.Add((baseTile.OriginZcm + baseTile.VertexZcm[i]) / CmPerMeter);
            }

            for (int i = 0; i < baseTile.TriangleCount; i++)
            {
                int a = baseTile.TriA[i];
                int b = baseTile.TriB[i];
                int c = baseTile.TriC[i];

                if (IsTriangleBlockedByObstacles(baseTile, a, b, c, obstacles, layerId))
                {
                    continue;
                }

                tris.Add(vertexBase + a);
                tris.Add(vertexBase + b);
                tris.Add(vertexBase + c);
            }
        }

        private static bool IsTriangleBlockedByObstacles(NavTile tile, int a, int b, int c, NavObstacleSet obstacles, string layerId)
        {
            if (obstacles?.Obstacles == null || obstacles.Obstacles.Count == 0) return false;

            int ax = tile.OriginXcm + tile.VertexXcm[a];
            int az = tile.OriginZcm + tile.VertexZcm[a];
            int bx = tile.OriginXcm + tile.VertexXcm[b];
            int bz = tile.OriginZcm + tile.VertexZcm[b];
            int cx = tile.OriginXcm + tile.VertexXcm[c];
            int cz = tile.OriginZcm + tile.VertexZcm[c];

            return NavObstacleGeometry.IsTriangleBlockedByObstacles(ax, az, bx, bz, cx, cz, obstacles, layerId);
        }

        private static void BuildNavTileFromDetailMesh(
            NavTile baseTile,
            int layer,
            uint tileVersion,
            ulong buildHash,
            RcPolyMeshDetail detail,
            RcPolyMesh polymesh,
            bool areaFromPolymesh,
            out NavTile tile)
        {
            var vertexIndex = new Dictionary<(int X, int Y, int Z), int>(detail.nverts);
            var vx = new List<int>(detail.nverts);
            var vy = new List<int>(detail.nverts);
            var vz = new List<int>(detail.nverts);
            var triA = new List<int>(detail.ntris);
            var triB = new List<int>(detail.ntris);
            var triC = new List<int>(detail.ntris);
            var triAreaIds = new List<byte>(detail.ntris);

            for (int m = 0; m < detail.nmeshes; m++)
            {
                int baseVert = detail.meshes[m * 4 + 0];
                int triBase = detail.meshes[m * 4 + 2];
                int triCount = detail.meshes[m * 4 + 3];
                byte polyArea = areaFromPolymesh && m < polymesh.npolys ? (byte)polymesh.areas[m] : (byte)0;

                for (int t = 0; t < triCount; t++)
                {
                    int triIndex = (triBase + t) * 4;
                    int da = detail.tris[triIndex + 0] + baseVert;
                    int db = detail.tris[triIndex + 1] + baseVert;
                    int dc = detail.tris[triIndex + 2] + baseVert;

                    int ia = GetOrAddVertex(detail, da, baseTile, vertexIndex, vx, vy, vz);
                    int ib = GetOrAddVertex(detail, db, baseTile, vertexIndex, vx, vy, vz);
                    int ic = GetOrAddVertex(detail, dc, baseTile, vertexIndex, vx, vy, vz);

                    if (ia == ib || ib == ic || ia == ic) continue;

                    triA.Add(ia);
                    triB.Add(ib);
                    triC.Add(ic);
                    triAreaIds.Add(areaFromPolymesh
                        ? polyArea
                        : ResolveAreaIdFromBaseTile(baseTile, vx[ia], vz[ia], vx[ib], vz[ib], vx[ic], vz[ic]));
                }
            }

            var n0 = new int[triA.Count];
            var n1 = new int[triA.Count];
            var n2 = new int[triA.Count];
            Array.Fill(n0, -1);
            Array.Fill(n1, -1);
            Array.Fill(n2, -1);
            BuildAdjacency(triA, triB, triC, n0, n1, n2);

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
                triAreaIds.ToArray(),
                baseTile.Portals);

            using var ms = new System.IO.MemoryStream();
            NavTileBinary.Write(ms, tmp);
            ms.Position = 0;
            tile = NavTileBinary.Read(ms);
        }

        private static byte ResolveAreaIdFromBaseTile(
            NavTile baseTile,
            int ax,
            int az,
            int bx,
            int bz,
            int cx,
            int cz)
        {
            float px = (ax + bx + cx) / 3f;
            float pz = (az + bz + cz) / 3f;

            for (int i = 0; i < baseTile.TriangleCount; i++)
            {
                int ia = baseTile.TriA[i];
                int ib = baseTile.TriB[i];
                int ic = baseTile.TriC[i];
                if (PointInTriangle2D(
                    px,
                    pz,
                    baseTile.VertexXcm[ia],
                    baseTile.VertexZcm[ia],
                    baseTile.VertexXcm[ib],
                    baseTile.VertexZcm[ib],
                    baseTile.VertexXcm[ic],
                    baseTile.VertexZcm[ic]))
                {
                    return baseTile.TriAreaIds[i];
                }
            }

            return 0;
        }

        private static bool PointInTriangle2D(
            float px,
            float pz,
            float ax,
            float az,
            float bx,
            float bz,
            float cx,
            float cz)
        {
            float v0x = cx - ax;
            float v0z = cz - az;
            float v1x = bx - ax;
            float v1z = bz - az;
            float v2x = px - ax;
            float v2z = pz - az;

            float dot00 = v0x * v0x + v0z * v0z;
            float dot01 = v0x * v1x + v0z * v1z;
            float dot02 = v0x * v2x + v0z * v2z;
            float dot11 = v1x * v1x + v1z * v1z;
            float dot12 = v1x * v2x + v1z * v2z;

            float denom = dot00 * dot11 - dot01 * dot01;
            if (MathF.Abs(denom) <= 1e-5f) return false;

            float invDenom = 1f / denom;
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
            const float epsilon = 0.001f;
            return u >= -epsilon && v >= -epsilon && u + v <= 1f + epsilon;
        }

        private static int GetOrAddVertex(
            RcPolyMeshDetail detail,
            int detailVertexIndex,
            NavTile baseTile,
            Dictionary<(int X, int Y, int Z), int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz)
        {
            int vi = detailVertexIndex * 3;
            float wx = detail.verts[vi + 0];
            float wy = detail.verts[vi + 1];
            float wz = detail.verts[vi + 2];

            int worldXcm = (int)MathF.Round(wx * CmPerMeter);
            int worldYcm = (int)MathF.Round(wy * CmPerMeter);
            int worldZcm = (int)MathF.Round(wz * CmPerMeter);

            int localXcm = worldXcm - baseTile.OriginXcm;
            int localZcm = worldZcm - baseTile.OriginZcm;

            var key = (localXcm, worldYcm, localZcm);
            if (vertexIndex.TryGetValue(key, out int existing)) return existing;

            int id = vx.Count;
            vx.Add(localXcm);
            vy.Add(worldYcm);
            vz.Add(localZcm);
            vertexIndex[key] = id;
            return id;
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
