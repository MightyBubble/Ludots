using System;
using System.Collections.Generic;
using System.IO;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Geometry;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace Ludots.NavBake.Recast
{
    /// <summary>
    /// Production Recast adapter over frozen triangle-surface input only.
    /// DotRecast uses floats and allocates; this is reported honestly and is not a 0GC path.
    /// </summary>
    public sealed class RecastNavBakeAlgorithm : INavBakeAlgorithm
    {
        public NavBakeAlgorithmKind Kind => NavBakeAlgorithmKind.Recast;

        public NavBakeAdapterCapabilities Capabilities =>
            NavBakeAdapterCapabilities.OfflineTriangleSurface |
            NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface;

        public bool SupportsMode(NavBakeMode mode)
        {
            return mode switch
            {
                NavBakeMode.Offline => true,
                NavBakeMode.RuntimeIncremental => true,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, $"Unknown nav bake mode '{mode}'.")
            };
        }

        public bool GuaranteesBitwiseDeterminism => false;

        public bool Supports3DMultiLayer => true;

        public bool IsZeroAllocationHotPath => false;

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
            return RecastNavTileBaker.TryBakeTriangleSurface(
                context,
                target,
                layer,
                navProfile,
                agentProfile,
                out tile,
                out detourTileBytes,
                out artifact);
        }
    }

    public static class RecastNavTileBaker
    {
        public static bool TryBakeTriangleSurface(
            NavBakeContext context,
            NavBakeTileCoord target,
            NavLayerConfig layer,
            NavMeshAgentProfileConfig navProfile,
            AgentProfileConfig agentProfile,
            out NavTile tile,
            out byte[] detourTileBytes,
            out NavBakeArtifact artifact)
        {
            tile = null!;
            detourTileBytes = Array.Empty<byte>();
            artifact = default;

            if (context == null) throw new ArgumentNullException(nameof(context));
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (navProfile == null) throw new ArgumentNullException(nameof(navProfile));
            if (agentProfile == null) throw new ArgumentNullException(nameof(agentProfile));

            if (context.InputKind != NavBakeInputKind.TriangleSurface)
            {
                throw new NavBakeUnsupportedInputException(
                    NavBakeAlgorithmKind.Recast,
                    NavBakeAdapterCapability.FormatInputKind(context.InputKind),
                    "RecastNavBakeAlgorithm declares triangle-surface capabilities only.");
            }

            NavTriangleSurfaceTileIndex surfaceIndex = context.RequireTriangleSurface();
            NavTriangleSurfaceTileGrid grid = surfaceIndex.Grid;
            NavTriangleSurfaceSnapshot surface = surfaceIndex.Surface;
            ReadOnlySpan<int> triangleIndices = surfaceIndex.GetTriangleIndices(target);

            int agentHeightCm = RequireExactPositiveIntCm(agentProfile.HeightCm, $"AgentProfile '{agentProfile.Id}'.heightCm");
            int agentRadiusCm = RequireExactNonNegativeIntCm(agentProfile.RadiusCm, $"AgentProfile '{agentProfile.Id}'.radiusCm");
            if (navProfile.MaxClimbCm < 0)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.profiles['{navProfile.Id}'].maxClimbCm must be >= 0.");
            }

            int originXcm = checked(grid.OriginXcm + checked(target.ChunkX * grid.TileWidthCm));
            int originZcm = checked(grid.OriginZcm + checked(target.ChunkY * grid.TileHeightCm));
            int tileWidthCm = grid.TileWidthCm;
            int tileHeightCm = grid.TileHeightCm;
            ulong buildHash = ComputeBuildConfigHash(context.BuildConfig, context.Config.Recast);

            try
            {
                RejectUnsupportedSolidOnlyHorizontalWalkSurfaces(surface, triangleIndices);

                // Keep authored walkable ground triangles intact. Obstacles are applied later via
                // Recast convex volumes (RC_NULL_AREA), not by deleting whole source triangles —
                // LogicTerrain emits two large tris per chunk, so triangle deletion would erase tiles.
                BuildRecastInputMesh(
                    surface,
                    triangleIndices,
                    out List<float> verts,
                    out List<int> tris,
                    out List<byte> areaIds,
                    out bool hasWalkCandidate);

                if (!hasWalkCandidate || tris.Count == 0)
                {
                    tile = NavValidEmptyTile.Create(
                        new NavTileId(target.ChunkX, target.ChunkY, layer.Layer),
                        context.TileVersion,
                        buildHash,
                        originXcm,
                        originZcm);
                    detourTileBytes = Array.Empty<byte>();
                    artifact = NavValidEmptyTile.CreateSuccessArtifact(tile, "No walkable triangles after Recast triangle-surface filtering.");
                    return true;
                }

                float tileMinX = originXcm / 100f;
                float tileMinZ = originZcm / 100f;
                float tileMaxX = checked(originXcm + tileWidthCm) / 100f;
                float tileMaxZ = checked(originZcm + tileHeightCm) / 100f;
                RcConfig rcCfg = BuildRcConfig(context, agentProfile, navProfile, tileMinX, tileMinZ, tileMaxX, tileMaxZ);

                var geom = new RcSampleInputGeomProvider(verts.ToArray(), tris.ToArray());
                RecastObstacleConvexVolumes.AddNullAreaVolumes(
                    geom,
                    context.Obstacles,
                    layer.Id,
                    rcCfg.Cs,
                    agentHeightCm,
                    agentRadiusCm,
                    tileMinX,
                    tileMinZ,
                    tileMaxX,
                    tileMaxZ,
                    borderWorldMeters: rcCfg.BorderSize * rcCfg.Cs);
                RcVec3f geomMin = geom.GetMeshBoundsMin();
                RcVec3f geomMax = geom.GetMeshBoundsMax();
                var tileBmin = new RcVec3f(tileMinX, geomMin.Y, tileMinZ);
                var tileBmax = new RcVec3f(
                    tileMinX + rcCfg.TileSizeX * rcCfg.Cs,
                    geomMax.Y,
                    tileMinZ + rcCfg.TileSizeZ * rcCfg.Cs);
                var bcfg = new RcBuilderConfig(rcCfg, tileBmin, tileBmax, tileX: 0, tileZ: 0);
                var rcBuilder = new RcBuilder();
                // DotRecast allocates float scratch and intermediate meshes; reported honestly (not 0GC).
                var rcResult = rcBuilder.Build(geom, bcfg, keepInterResults: false);

                if (rcResult?.Mesh == null)
                {
                    artifact = new NavBakeArtifact(
                        new NavTileId(target.ChunkX, target.ChunkY, layer.Layer),
                        context.TileVersion,
                        NavBakeStage.Triangulate,
                        NavBakeErrorCode.TriangulationFailed,
                        "Recast produced a null poly mesh.",
                        0, 0, 0, 0);
                    return false;
                }

                if (rcResult.Mesh.npolys <= 0)
                {
                    tile = NavValidEmptyTile.Create(
                        new NavTileId(target.ChunkX, target.ChunkY, layer.Layer),
                        context.TileVersion,
                        buildHash,
                        originXcm,
                        originZcm);
                    detourTileBytes = Array.Empty<byte>();
                    artifact = NavValidEmptyTile.CreateSuccessArtifact(tile, "Recast produced an empty walkable mesh for the target tile.");
                    return true;
                }

                if (rcResult.MeshDetail == null || rcResult.MeshDetail.ntris <= 0)
                {
                    tile = NavValidEmptyTile.Create(
                        new NavTileId(target.ChunkX, target.ChunkY, layer.Layer),
                        context.TileVersion,
                        buildHash,
                        originXcm,
                        originZcm);
                    detourTileBytes = Array.Empty<byte>();
                    artifact = NavValidEmptyTile.CreateSuccessArtifact(tile, "Recast produced an empty detail mesh for the target tile.");
                    return true;
                }

                PrepareDetourPolygons(rcResult.Mesh, areaIds, verts, tris);

                int minWalkableUpDotQ1M = LayeredSpanSlopeQ1M.CompileMinWalkableUpDotQ1M(
                    navProfile.MaxSlopeDeg,
                    $"NavMeshBakeConfig.profiles['{navProfile.Id}'].maxSlopeDeg");

                BuildNavTileFromDetailMesh(
                    target.ChunkX,
                    target.ChunkY,
                    layer.Layer,
                    context.TileVersion,
                    buildHash,
                    originXcm,
                    originZcm,
                    tileWidthCm,
                    tileHeightCm,
                    rcResult.MeshDetail,
                    surface,
                    triangleIndices,
                    context.Obstacles,
                    layer.Id,
                    agentHeightCm,
                    agentRadiusCm,
                    minWalkableUpDotQ1M,
                    navProfile.MaxClimbCm,
                    context.Config.Recast.RasterCellSizeCm,
                    out tile);

                // Detour external links are emitted only through NavBorderPortal proof
                // (DetourNavQueryEngine.ToDetourNeighbor). Never invent links from bare border edges.
                detourTileBytes = tile.TriangleCount == 0
                    ? Array.Empty<byte>()
                    : DetourNavQueryEngine.BuildDetourTileBytes(tile, tileWidthCm, tileHeightCm);

                artifact = new NavBakeArtifact(
                    tile.TileId,
                    tile.TileVersion,
                    NavBakeStage.Serialize,
                    NavBakeErrorCode.None,
                    "",
                    tile.TriangleCount,
                    tile.VertexCount,
                    tile.TriangleCount,
                    tile.PortalCount);
                return true;
            }
            catch (NavBakeUnsupportedInputException)
            {
                throw;
            }
            catch (Exception ex)
            {
                artifact = new NavBakeArtifact(
                    new NavTileId(target.ChunkX, target.ChunkY, layer.Layer),
                    context.TileVersion,
                    NavBakeStage.Serialize,
                    NavBakeErrorCode.SerializationFailed,
                    ex.Message,
                    0, 0, 0, 0);
                tile = null!;
                detourTileBytes = Array.Empty<byte>();
                return false;
            }
        }

        private static void RejectUnsupportedSolidOnlyHorizontalWalkSurfaces(
            NavTriangleSurfaceSnapshot surface,
            ReadOnlySpan<int> triangleIndices)
        {
            // DotRecast only marks RC_WALKABLE_AREA from walkable rasterization of input tris.
            // A Solid-only near-horizontal surface that should remain non-walkable is accepted
            // (it never becomes walkable). Reject only pathological cases where Solid-only geometry
            // is the sole horizontal deck AND would be indistinguishable from a walk deck if
            // incorrectly marked walkable — we never mark Solid-only as walkable, so no reject needed
            // for normal Solid walls/ceilings.
            //
            // Explicit reject: Solid-only triangle that is slope-walkable (up-dot high) AND has no
            // WalkCandidate sibling covering the same XZ footprint would become walkable under
            // DotRecast if we fed it as walkable. We feed Solid-only as non-walkable geometry only
            // via inclusion in the mesh without walkable area — DotRecast still rasterizes all tris
            // as walkable by default. Therefore Solid-only horizontal decks are unsupported.
            ReadOnlySpan<int> vx = surface.VertexXcm;
            ReadOnlySpan<int> vy = surface.VertexYcm;
            ReadOnlySpan<int> vz = surface.VertexZcm;
            ReadOnlySpan<int> ta = surface.TriA;
            ReadOnlySpan<int> tb = surface.TriB;
            ReadOnlySpan<int> tc = surface.TriC;
            ReadOnlySpan<NavTriangleSurfaceFlags> flags = surface.TriFlags;
            ReadOnlySpan<int> stable = surface.TriStableIds;

            for (int i = 0; i < triangleIndices.Length; i++)
            {
                int tri = triangleIndices[i];
                NavTriangleSurfaceFlags f = flags[tri];
                if ((f & NavTriangleSurfaceFlags.WalkCandidate) != 0)
                {
                    continue;
                }

                int a = ta[tri];
                int b = tb[tri];
                int c = tc[tri];
                long abx = (long)vx[b] - vx[a];
                long aby = (long)vy[b] - vy[a];
                long abz = (long)vz[b] - vz[a];
                long acx = (long)vx[c] - vx[a];
                long acy = (long)vy[c] - vy[a];
                long acz = (long)vz[c] - vz[a];
                // Normal via cross product.
                long nx = (aby * acz) - (abz * acy);
                long ny = (abz * acx) - (abx * acz);
                long nz = (abx * acy) - (aby * acx);
                long absNy = ny < 0 ? -ny : ny;
                long absNx = nx < 0 ? -nx : nx;
                long absNz = nz < 0 ? -nz : nz;
                // Near-horizontal: |ny| dominates and area is non-zero.
                if (absNy > 0 && absNy >= absNx && absNy >= absNz)
                {
                    throw new NavBakeUnsupportedInputException(
                        NavBakeAlgorithmKind.Recast,
                        $"triangleStableId={stable[tri]}",
                        "DotRecast cannot express a slope-walkable Solid-only horizontal surface without making it walkable; " +
                        "mark WalkCandidate|Solid or omit the deck from Recast input.");
                }
            }
        }

        private static void BuildRecastInputMesh(
            NavTriangleSurfaceSnapshot surface,
            ReadOnlySpan<int> triangleIndices,
            out List<float> verts,
            out List<int> tris,
            out List<byte> areaIds,
            out bool hasWalkCandidate)
        {
            // Capacity is tile-local (CSR triangleIndices), never whole-world VertexCount.
            // Open-world surfaces keep thousands of verts; pre-sizing to that forces LOH churn per
            // local tile bake and unequal RTS vs open heap pressure for identical tile work.
            int localTriCount = triangleIndices.Length;
            int vertexCapacityHint = localTriCount * 3;
            if (vertexCapacityHint > surface.VertexCount)
            {
                vertexCapacityHint = surface.VertexCount;
            }

            verts = new List<float>(vertexCapacityHint * 3);
            tris = new List<int>(localTriCount * 3);
            areaIds = new List<byte>(localTriCount);
            hasWalkCandidate = false;

            var vertexMap = new Dictionary<(int X, int Y, int Z), int>(vertexCapacityHint);
            ReadOnlySpan<int> vx = surface.VertexXcm;
            ReadOnlySpan<int> vy = surface.VertexYcm;
            ReadOnlySpan<int> vz = surface.VertexZcm;
            ReadOnlySpan<int> ta = surface.TriA;
            ReadOnlySpan<int> tb = surface.TriB;
            ReadOnlySpan<int> tc = surface.TriC;
            ReadOnlySpan<byte> areas = surface.TriAreaIds;
            ReadOnlySpan<NavTriangleSurfaceFlags> flags = surface.TriFlags;

            for (int i = 0; i < triangleIndices.Length; i++)
            {
                int tri = triangleIndices[i];
                int a = ta[tri];
                int b = tb[tri];
                int c = tc[tri];
                NavTriangleSurfaceFlags f = flags[tri];
                bool walk = (f & NavTriangleSurfaceFlags.WalkCandidate) != 0;

                long abx = (long)vx[b] - vx[a];
                long aby = (long)vy[b] - vy[a];
                long abz = (long)vz[b] - vz[a];
                long acx = (long)vx[c] - vx[a];
                long acy = (long)vy[c] - vy[a];
                long acz = (long)vz[c] - vz[a];
                long nx = (aby * acz) - (abz * acy);
                long ny = (abz * acx) - (abx * acz);
                long nz = (abx * acy) - (aby * acx);
                long absNy = ny < 0 ? -ny : ny;
                long absNx = nx < 0 ? -nx : nx;
                long absNz = nz < 0 ? -nz : nz;

                if (!walk)
                {
                    // Include Solid-only non-horizontal geometry for raster blocking.
                    // Horizontal Solid-only already rejected above.
                    if (absNy >= absNx && absNy >= absNz)
                    {
                        // Horizontal solid already rejected; skip any residual.
                        continue;
                    }
                }
                else if (ny < 0)
                {
                    // DotRecast MarkWalkableTriangles rejects downward faces. Flip winding so
                    // authored WalkCandidate decks remain walkable regardless of source winding.
                    (b, c) = (c, b);
                }

                int ia = MapVertex(vx[a], vy[a], vz[a], vertexMap, verts);
                int ib = MapVertex(vx[b], vy[b], vz[b], vertexMap, verts);
                int ic = MapVertex(vx[c], vy[c], vz[c], vertexMap, verts);
                if (ia == ib || ib == ic || ia == ic)
                {
                    continue;
                }

                tris.Add(ia);
                tris.Add(ib);
                tris.Add(ic);
                areaIds.Add(areas[tri]);
                if (walk)
                {
                    hasWalkCandidate = true;
                }
            }
        }

        private static int MapVertex(
            int xcm,
            int ycm,
            int zcm,
            Dictionary<(int X, int Y, int Z), int> map,
            List<float> verts)
        {
            var key = (xcm, ycm, zcm);
            if (map.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int id = verts.Count / 3;
            map.Add(key, id);
            verts.Add(xcm / 100f);
            verts.Add(ycm / 100f);
            verts.Add(zcm / 100f);
            return id;
        }

        private static void PrepareDetourPolygons(RcPolyMesh mesh, List<byte> sourceAreaIds, List<float> verts, List<int> tris)
        {
            if (mesh.flags == null || mesh.flags.Length < mesh.npolys)
            {
                mesh.flags = new int[mesh.npolys];
            }

            for (int i = 0; i < mesh.npolys; i++)
            {
                mesh.flags[i] = 1;
                byte areaId = ResolveAreaIdFromSource(mesh, i, sourceAreaIds, verts, tris);
                if (areaId >= DtDetour.DT_MAX_AREAS)
                {
                    throw new InvalidOperationException(
                        $"Recast polygon {i} area id {areaId} exceeds Detour max area id {DtDetour.DT_MAX_AREAS - 1}.");
                }

                mesh.areas[i] = areaId;
            }
        }

        private static byte ResolveAreaIdFromSource(
            RcPolyMesh mesh,
            int polyIndex,
            List<byte> sourceAreaIds,
            List<float> verts,
            List<int> tris)
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

            if (count == 0 || sourceAreaIds.Count == 0)
            {
                return 0;
            }

            float px = sx / count;
            float pz = sz / count;
            for (int i = 0; i < sourceAreaIds.Count; i++)
            {
                int ia = tris[i * 3 + 0] * 3;
                int ib = tris[i * 3 + 1] * 3;
                int ic = tris[i * 3 + 2] * 3;
                if (PointInTriangle2D(
                        px, pz,
                        verts[ia], verts[ia + 2],
                        verts[ib], verts[ib + 2],
                        verts[ic], verts[ic + 2]))
                {
                    return sourceAreaIds[i];
                }
            }

            return 0;
        }

        private static ulong ComputeBuildConfigHash(NavBuildConfig buildConfig, NavRecastConfig? recast)
        {
            if (recast == null)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.recast is required for Recast tile build hash.");
            }

            ulong h = buildConfig.ComputeHash();
            h = Mix(h, recast.RasterCellSizeCm);
            h = Mix(h, recast.RasterCellHeightCm);
            return h;
        }

        private static ulong Mix(ulong hash, int value)
            => (hash ^ (ulong)(uint)value) * 1099511628211UL;

        private static RcConfig BuildRcConfig(
            NavBakeContext context,
            AgentProfileConfig agentProfile,
            NavMeshAgentProfileConfig navProfile,
            float tileMinX,
            float tileMinZ,
            float tileMaxX,
            float tileMaxZ)
        {
            NavRecastConfig recast = context.Config.Recast
                ?? throw new InvalidOperationException("NavMeshBakeConfig.recast is required.");
            recast.Validate();

            float radius = agentProfile.RadiusCm / 100f;
            float height = agentProfile.HeightCm / 100f;
            float maxClimb = navProfile.MaxClimbCm / 100f;
            float maxSlope = navProfile.MaxSlopeDeg;

            // Raster resolution is data-driven. Border/erosion still use the real agent radius.
            float cellSize = recast.RasterCellSizeCm / 100f;
            float cellHeight = recast.RasterCellHeightCm / 100f;
            int tileSizeX = Math.Max(1, (int)MathF.Ceiling((tileMaxX - tileMinX) / cellSize));
            int tileSizeZ = Math.Max(1, (int)MathF.Ceiling((tileMaxZ - tileMinZ) / cellSize));
            int borderSize = RcConfig.CalcBorder(radius, cellSize);

            // Recast erosion owns clearance from triangle-mesh walls, cliffs, and world edges.
            // Convex obstacle volumes are marked after erosion, so they are expanded separately
            // by RecastObstacleConvexVolumes and do not receive this radius a second time.
            return new RcConfig(
                true,
                tileSizeX,
                tileSizeZ,
                borderSize,
                DotRecast.Recast.RcPartition.WATERSHED,
                cellSize, cellHeight,
                maxSlope, height, agentRadius: radius, maxClimb,
                8 * 8 * cellSize * cellSize,
                20 * 20 * cellSize * cellSize,
                12f, 1.3f,
                6,
                6f, 1f,
                true, true, true,
                new RcAreaModification(RcRecast.RC_WALKABLE_AREA), true);
        }

        private static void BuildNavTileFromDetailMesh(
            int chunkX,
            int chunkY,
            int layer,
            uint tileVersion,
            ulong buildHash,
            int originXcm,
            int originZcm,
            int tileWidthCm,
            int tileHeightCm,
            RcPolyMeshDetail detail,
            NavTriangleSurfaceSnapshot surface,
            ReadOnlySpan<int> triangleIndices,
            INavObstacleSource obstacles,
            string layerId,
            int agentHeightCm,
            int agentRadiusCm,
            int minWalkableUpDotQ1M,
            int maxClimbCm,
            int rasterCellSizeCm,
            out NavTile tile)
        {
            NavBorderPortalCoordinateContract.RequireTileExtentFitsPortalCoordinates(
                tileWidthCm,
                tileHeightCm,
                "RecastNavTileBaker.BuildNavTileFromDetailMesh");

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

                for (int t = 0; t < triCount; t++)
                {
                    int triIndex = (triBase + t) * 4;
                    int da = detail.tris[triIndex + 0] + baseVert;
                    int db = detail.tris[triIndex + 1] + baseVert;
                    int dc = detail.tris[triIndex + 2] + baseVert;

                    int ia = GetOrAddVertex(detail, da, originXcm, originZcm, vertexIndex, vx, vy, vz);
                    int ib = GetOrAddVertex(detail, db, originXcm, originZcm, vertexIndex, vx, vy, vz);
                    int ic = GetOrAddVertex(detail, dc, originXcm, originZcm, vertexIndex, vx, vy, vz);
                    if (ia == ib || ib == ic || ia == ic) continue;

                    // Keep only triangles whose centroid lies inside the target tile XZ.
                    int cx = (vx[ia] + vx[ib] + vx[ic]) / 3;
                    int cz = (vz[ia] + vz[ib] + vz[ic]) / 3;
                    if (cx < 0 || cz < 0 || cx >= tileWidthCm || cz >= tileHeightCm)
                    {
                        continue;
                    }

                    triA.Add(ia);
                    triB.Add(ib);
                    triC.Add(ic);
                    triAreaIds.Add(ResolveAreaIdFromSurface(
                        surface,
                        triangleIndices,
                        originXcm + cx,
                        originZcm + cz));
                }
            }

            if (triA.Count == 0)
            {
                tile = NavValidEmptyTile.Create(
                    new NavTileId(chunkX, chunkY, layer),
                    tileVersion,
                    buildHash,
                    originXcm,
                    originZcm);
                return;
            }

            var n0 = new int[triA.Count];
            var n1 = new int[triA.Count];
            var n2 = new int[triA.Count];
            Array.Fill(n0, -1);
            Array.Fill(n1, -1);
            Array.Fill(n2, -1);
            BuildAdjacency(triA, triB, triC, n0, n1, n2);

            // Portal geometry comes from Recast open mesh edges (tile-local XYZ that Detour links).
            // Cross-tile proof comes only from WalkCandidate surface evidence on the exact world
            // border plane with neighbor half-space + positive along overlap + Y climb compatibility.
            NavBorderPortal[] portals = BuildBorderPortalsFromMesh(
                vx, vy, vz, triA, triB, triC, n0, n1, n2,
                originXcm, originZcm,
                tileWidthCm, tileHeightCm,
                obstacles, layerId, agentHeightCm, agentRadiusCm, minWalkableUpDotQ1M, maxClimbCm,
                rasterCellSizeCm,
                surface, triangleIndices);

            var tmp = new NavTile(
                new NavTileId(chunkX, chunkY, layer),
                tileVersion,
                buildHash,
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
                triAreaIds.ToArray(),
                portals);

            using var ms = new MemoryStream();
            NavTileBinary.Write(ms, tmp);
            ms.Position = 0;
            tile = NavTileBinary.Read(ms);
        }

        private static NavBorderPortal[] BuildBorderPortalsFromMesh(
            List<int> vx,
            List<int> vy,
            List<int> vz,
            List<int> triA,
            List<int> triB,
            List<int> triC,
            int[] n0,
            int[] n1,
            int[] n2,
            int originXcm,
            int originZcm,
            int tileWidthCm,
            int tileHeightCm,
            INavObstacleSource obstacles,
            string layerId,
            int agentHeightCm,
            int agentRadiusCm,
            int minWalkableUpDotQ1M,
            int maxClimbCm,
            int rasterCellSizeCm,
            NavTriangleSurfaceSnapshot surface,
            ReadOnlySpan<int> triangleIndices)
        {
            // Recast walkable erosion insets open edges. Portal XYZ/U/V therefore come from those
            // mesh edges so Detour can link them; cross-tile acceptance still requires surface
            // neighbor half-space proof on the exact world border plane (not current-side-only edges).
            NavBorderPortalCoordinateContract.RequireTileExtentFitsPortalCoordinates(
                tileWidthCm,
                tileHeightCm,
                "RecastNavTileBaker.BuildBorderPortalsFromMesh");

            var portals = new List<NavBorderPortal>();
            for (int t = 0; t < triA.Count; t++)
            {
                TryAddPortalForEdge(t, 0, triA[t], triB[t], n0[t], vx, vy, vz, originXcm, originZcm, tileWidthCm, tileHeightCm, obstacles, layerId, agentHeightCm, agentRadiusCm, minWalkableUpDotQ1M, maxClimbCm, rasterCellSizeCm, surface, triangleIndices, portals);
                TryAddPortalForEdge(t, 1, triB[t], triC[t], n1[t], vx, vy, vz, originXcm, originZcm, tileWidthCm, tileHeightCm, obstacles, layerId, agentHeightCm, agentRadiusCm, minWalkableUpDotQ1M, maxClimbCm, rasterCellSizeCm, surface, triangleIndices, portals);
                TryAddPortalForEdge(t, 2, triC[t], triA[t], n2[t], vx, vy, vz, originXcm, originZcm, tileWidthCm, tileHeightCm, obstacles, layerId, agentHeightCm, agentRadiusCm, minWalkableUpDotQ1M, maxClimbCm, rasterCellSizeCm, surface, triangleIndices, portals);
            }

            portals.Sort(ComparePortals);
            return portals.ToArray();
        }

        private static void TryAddPortalForEdge(
            int triIndex,
            int edgeIndex,
            int ia,
            int ib,
            int neighbor,
            List<int> vx,
            List<int> vy,
            List<int> vz,
            int originXcm,
            int originZcm,
            int tileWidthCm,
            int tileHeightCm,
            INavObstacleSource obstacles,
            string layerId,
            int agentHeightCm,
            int agentRadiusCm,
            int minWalkableUpDotQ1M,
            int maxClimbCm,
            int rasterCellSizeCm,
            NavTriangleSurfaceSnapshot surface,
            ReadOnlySpan<int> triangleIndices,
            List<NavBorderPortal> portals)
        {
            _ = triIndex;
            _ = edgeIndex;
            if (neighbor >= 0) return;

            int ax = vx[ia];
            int ay = vy[ia];
            int az = vz[ia];
            int bx = vx[ib];
            int by = vy[ib];
            int bz = vz[ib];

            int bandCm = ComputeRecastBorderBandCm(agentRadiusCm, rasterCellSizeCm);
            if (!TryClassifyBorder(ax, az, bx, bz, tileWidthCm, tileHeightCm, bandCm, out NavPortalSide side, out short u0, out short v0, out short u1, out short v1))
            {
                return;
            }

            int along0;
            int along1;
            if (side == NavPortalSide.West || side == NavPortalSide.East)
            {
                along0 = az;
                along1 = bz;
            }
            else
            {
                along0 = ax;
                along1 = bx;
            }

            if (along0 == along1)
            {
                return; // point/corner
            }

            int worldAx = checked(originXcm + ax);
            int worldAz = checked(originZcm + az);
            int worldBx = checked(originXcm + bx);
            int worldBz = checked(originZcm + bz);
            int boundaryWorld = GetWorldBoundaryCoordinate(side, originXcm, originZcm, tileWidthCm, tileHeightCm);

            if (!HasWorldBoundaryNeighborEvidence(
                    surface,
                    triangleIndices,
                    obstacles,
                    layerId,
                    agentHeightCm,
                    agentRadiusCm,
                    minWalkableUpDotQ1M,
                    side,
                    boundaryWorld,
                    worldAx, ay, worldAz,
                    worldBx, by, worldBz,
                    maxClimbCm))
            {
                return;
            }

            int len = NavSegmentMetrics.RoundEuclideanLengthCm(ax, ay, az, bx, by, bz);
            int clearance = Math.Max(0, len / 2);
            if (clearance < agentRadiusCm)
            {
                return;
            }

            int lx = ax;
            int ly = ay;
            int lz = az;
            int rx = bx;
            int ry = by;
            int rz = bz;
            short ou0 = u0;
            short ov0 = v0;
            short ou1 = u1;
            short ov1 = v1;
            if (along1 < along0)
            {
                (lx, ly, lz, rx, ry, rz) = (rx, ry, rz, lx, ly, lz);
                (ou0, ov0, ou1, ov1) = (ou1, ov1, ou0, ov0);
            }

            portals.Add(new NavBorderPortal(side, ou0, ov0, ou1, ov1, lx, ly, lz, rx, ry, rz, clearance));
        }

        private static int GetWorldBoundaryCoordinate(
            NavPortalSide side,
            int originXcm,
            int originZcm,
            int tileWidthCm,
            int tileHeightCm)
        {
            return side switch
            {
                NavPortalSide.West => originXcm,
                NavPortalSide.East => checked(originXcm + tileWidthCm),
                NavPortalSide.North => originZcm,
                NavPortalSide.South => checked(originZcm + tileHeightCm),
                _ => throw new InvalidOperationException($"Unknown NavPortalSide '{side}'.")
            };
        }

        private static bool HasWorldBoundaryNeighborEvidence(
            NavTriangleSurfaceSnapshot surface,
            ReadOnlySpan<int> triangleIndices,
            INavObstacleSource obstacles,
            string layerId,
            int agentHeightCm,
            int agentRadiusCm,
            int minWalkableUpDotQ1M,
            NavPortalSide side,
            int boundaryWorld,
            int ax,
            int ay,
            int az,
            int bx,
            int by,
            int bz,
            int maxClimbCm)
        {
            ReadOnlySpan<int> svx = surface.VertexXcm;
            ReadOnlySpan<int> svy = surface.VertexYcm;
            ReadOnlySpan<int> svz = surface.VertexZcm;
            ReadOnlySpan<int> ta = surface.TriA;
            ReadOnlySpan<int> tb = surface.TriB;
            ReadOnlySpan<int> tc = surface.TriC;

            int edgeAlongA = side is NavPortalSide.West or NavPortalSide.East ? az : ax;
            int edgeAlongB = side is NavPortalSide.West or NavPortalSide.East ? bz : bx;
            GetAlong(side, ax, az, bx, bz, out int eMin, out int eMax);
            if (eMax <= eMin)
            {
                return false;
            }

            for (int i = 0; i < triangleIndices.Length; i++)
            {
                int tri = triangleIndices[i];
                // Recast keeps surface triangles and punches RC_NULL_AREA holes. Whole-triangle obstacle
                // rejection would erase free side-route portal evidence on coarse tile floors when a
                // corner obstacle only seals a local interval. Clip obstacles along the border instead.
                if (!TriangleSurfaceWalkability.IsWalkableTriangleIgnoringObstacles(
                        tri,
                        svx,
                        svy,
                        svz,
                        ta,
                        tb,
                        tc,
                        surface.TriFlags,
                        minWalkableUpDotQ1M,
                        agentHeightCm,
                        triangleIndices))
                {
                    continue;
                }

                int ia = ta[tri];
                int ib = tb[tri];
                int ic = tc[tri];
                int tx0 = svx[ia];
                int ty0 = svy[ia];
                int tz0 = svz[ia];
                int tx1 = svx[ib];
                int ty1 = svy[ib];
                int tz1 = svz[ib];
                int tx2 = svx[ic];
                int ty2 = svy[ic];
                int tz2 = svz[ic];

                if (!TriangleTouchesWorldBoundaryPlane(side, boundaryWorld, tx0, tz0, tx1, tz1, tx2, tz2))
                {
                    continue;
                }

                if (!TriangleProvidesNeighborHalfSpaceEvidence(
                        side,
                        boundaryWorld,
                        tx0, ty0, tz0,
                        tx1, ty1, tz1,
                        tx2, ty2, tz2,
                        out int evidenceMinAlong,
                        out int evidenceMaxAlong,
                        out int evidenceYAtMinAlong,
                        out int evidenceYAtMaxAlong))
                {
                    continue;
                }

                int overlapMin = eMin > evidenceMinAlong ? eMin : evidenceMinAlong;
                int overlapMax = eMax < evidenceMaxAlong ? eMax : evidenceMaxAlong;
                if (overlapMax <= overlapMin)
                {
                    continue;
                }

                int surfaceMinY = ty0;
                if (ty1 < surfaceMinY) surfaceMinY = ty1;
                if (ty2 < surfaceMinY) surfaceMinY = ty2;
                if (NavTriangleObstaclePredicate.IsBoundaryAlongIntervalFullyBlocked(
                        side,
                        boundaryWorld,
                        overlapMin,
                        overlapMax,
                        surfaceMinY,
                        obstacles,
                        layerId,
                        agentHeightCm,
                        agentRadiusCm))
                {
                    continue;
                }

                // Climb must be judged on the actual overlapping along interval, never whole-span min/max.
                SampleYRangeOnAlongInterval(
                    edgeAlongA, ay, edgeAlongB, by, overlapMin, overlapMax, out int edgeMinY, out int edgeMaxY);
                SampleYRangeOnAlongInterval(
                    evidenceMinAlong,
                    evidenceYAtMinAlong,
                    evidenceMaxAlong,
                    evidenceYAtMaxAlong,
                    overlapMin,
                    overlapMax,
                    out int evidenceMinY,
                    out int evidenceMaxY);
                int climb = edgeMinY > evidenceMaxY
                    ? edgeMinY - evidenceMaxY
                    : (evidenceMinY > edgeMaxY ? evidenceMinY - edgeMaxY : 0);
                if (climb <= maxClimbCm)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SampleYRangeOnAlongInterval(
            int alongA,
            int yA,
            int alongB,
            int yB,
            int overlapMin,
            int overlapMax,
            out int minY,
            out int maxY)
        {
            int y0 = InterpolateYAtAlong(alongA, yA, alongB, yB, overlapMin);
            int y1 = InterpolateYAtAlong(alongA, yA, alongB, yB, overlapMax);
            if (y0 <= y1)
            {
                minY = y0;
                maxY = y1;
            }
            else
            {
                minY = y1;
                maxY = y0;
            }
        }

        private static int InterpolateYAtAlong(int alongA, int yA, int alongB, int yB, int targetAlong)
        {
            if (alongA == alongB)
            {
                return yA;
            }

            Int128 denom = (Int128)alongB - alongA;
            Int128 num = ((Int128)yA * denom) + (((Int128)yB - yA) * ((Int128)targetAlong - alongA));
            Int128 y = DivideRoundHalfAwayFromZero(num, denom);
            if (y < int.MinValue || y > int.MaxValue)
            {
                throw new OverflowException("Recast portal Y interpolation overflows int centimetres.");
            }

            return (int)y;
        }

        private static bool TriangleTouchesWorldBoundaryPlane(
            NavPortalSide side,
            int boundaryWorld,
            int x0, int z0,
            int x1, int z1,
            int x2, int z2)
        {
            if (side is NavPortalSide.West or NavPortalSide.East)
            {
                int minX = Min3(x0, x1, x2);
                int maxX = Max3(x0, x1, x2);
                return minX <= boundaryWorld && boundaryWorld <= maxX;
            }

            int minZ = Min3(z0, z1, z2);
            int maxZ = Max3(z0, z1, z2);
            return minZ <= boundaryWorld && boundaryWorld <= maxZ;
        }

        private static bool TriangleProvidesNeighborHalfSpaceEvidence(
            NavPortalSide side,
            int boundaryWorld,
            int x0, int y0, int z0,
            int x1, int y1, int z1,
            int x2, int y2, int z2,
            out int evidenceMinAlong,
            out int evidenceMaxAlong,
            out int evidenceMinY,
            out int evidenceMaxY)
        {
            evidenceMinAlong = 0;
            evidenceMaxAlong = 0;
            evidenceMinY = 0;
            evidenceMaxY = 0;

            bool hasBoundaryEdge = false;
            int edgeMinAlong = 0;
            int edgeMaxAlong = 0;
            int edgeMinY = 0;
            int edgeMaxY = 0;
            AccumulateExactBoundaryEdge(side, boundaryWorld, x0, y0, z0, x1, y1, z1, ref hasBoundaryEdge, ref edgeMinAlong, ref edgeMaxAlong, ref edgeMinY, ref edgeMaxY);
            AccumulateExactBoundaryEdge(side, boundaryWorld, x1, y1, z1, x2, y2, z2, ref hasBoundaryEdge, ref edgeMinAlong, ref edgeMaxAlong, ref edgeMinY, ref edgeMaxY);
            AccumulateExactBoundaryEdge(side, boundaryWorld, x2, y2, z2, x0, y0, z0, ref hasBoundaryEdge, ref edgeMinAlong, ref edgeMaxAlong, ref edgeMinY, ref edgeMaxY);

            bool extendsIntoNeighbor =
                IsInNeighborHalfSpace(side, boundaryWorld, x0, z0) ||
                IsInNeighborHalfSpace(side, boundaryWorld, x1, z1) ||
                IsInNeighborHalfSpace(side, boundaryWorld, x2, z2);

            // An edge that merely lies on the boundary can belong entirely to this tile.
            // Portal evidence must always contain positive area in the neighbour half-space.
            if (!extendsIntoNeighbor)
            {
                return false;
            }

            if (hasBoundaryEdge && edgeMaxAlong > edgeMinAlong)
            {
                evidenceMinAlong = edgeMinAlong;
                evidenceMaxAlong = edgeMaxAlong;
                evidenceMinY = edgeMinY;
                evidenceMaxY = edgeMaxY;
                return true;
            }

            // Neighbour half-space without an exact boundary edge: intersect all three edges with
            // the exact world plane. The rounded centimetre endpoints must retain positive length.
            if (!TryCollectPlaneCoverage(
                    side,
                    boundaryWorld,
                    x0, y0, z0,
                    x1, y1, z1,
                    x2, y2, z2,
                    out evidenceMinAlong,
                    out evidenceMaxAlong,
                    out evidenceMinY,
                    out evidenceMaxY))
            {
                return false;
            }

            return evidenceMaxAlong > evidenceMinAlong;
        }

        private static void AccumulateExactBoundaryEdge(
            NavPortalSide side,
            int boundaryWorld,
            int ax, int ay, int az,
            int bx, int by, int bz,
            ref bool initialized,
            ref int minAlong,
            ref int maxAlong,
            ref int minY,
            ref int maxY)
        {
            bool onPlane = side is NavPortalSide.West or NavPortalSide.East
                ? ax == boundaryWorld && bx == boundaryWorld
                : az == boundaryWorld && bz == boundaryWorld;
            if (!onPlane)
            {
                return;
            }

            GetAlong(side, ax, az, bx, bz, out int a0, out int a1);
            if (a1 <= a0)
            {
                return;
            }

            if (!initialized)
            {
                minAlong = int.MaxValue;
                maxAlong = int.MinValue;
                minY = 0;
                maxY = 0;
                initialized = true;
            }

            int alongA = side is NavPortalSide.West or NavPortalSide.East ? az : ax;
            int alongB = side is NavPortalSide.West or NavPortalSide.East ? bz : bx;
            AccumulatePlanePoint(alongA, ay, ref minAlong, ref maxAlong, ref minY, ref maxY);
            AccumulatePlanePoint(alongB, by, ref minAlong, ref maxAlong, ref minY, ref maxY);
        }

        private static bool TryCollectPlaneCoverage(
            NavPortalSide side,
            int boundaryWorld,
            int x0, int y0, int z0,
            int x1, int y1, int z1,
            int x2, int y2, int z2,
            out int minAlong,
            out int maxAlong,
            out int minY,
            out int maxY)
        {
            minAlong = int.MaxValue;
            maxAlong = int.MinValue;
            minY = 0;
            maxY = 0;
            bool any = false;

            any |= ConsiderPlaneVertex(side, boundaryWorld, x0, y0, z0, ref minAlong, ref maxAlong, ref minY, ref maxY);
            any |= ConsiderPlaneVertex(side, boundaryWorld, x1, y1, z1, ref minAlong, ref maxAlong, ref minY, ref maxY);
            any |= ConsiderPlaneVertex(side, boundaryWorld, x2, y2, z2, ref minAlong, ref maxAlong, ref minY, ref maxY);
            any |= ConsiderPlaneCrossing(side, boundaryWorld, x0, y0, z0, x1, y1, z1, ref minAlong, ref maxAlong, ref minY, ref maxY);
            any |= ConsiderPlaneCrossing(side, boundaryWorld, x1, y1, z1, x2, y2, z2, ref minAlong, ref maxAlong, ref minY, ref maxY);
            any |= ConsiderPlaneCrossing(side, boundaryWorld, x2, y2, z2, x0, y0, z0, ref minAlong, ref maxAlong, ref minY, ref maxY);
            return any && maxAlong > minAlong;
        }

        private static bool ConsiderPlaneVertex(
            NavPortalSide side,
            int boundaryWorld,
            int x,
            int y,
            int z,
            ref int minAlong,
            ref int maxAlong,
            ref int minY,
            ref int maxY)
        {
            bool onPlane = side is NavPortalSide.West or NavPortalSide.East
                ? x == boundaryWorld
                : z == boundaryWorld;
            if (!onPlane)
            {
                return false;
            }

            int along = side is NavPortalSide.West or NavPortalSide.East ? z : x;
            AccumulatePlanePoint(along, y, ref minAlong, ref maxAlong, ref minY, ref maxY);
            return true;
        }

        private static bool ConsiderPlaneCrossing(
            NavPortalSide side,
            int boundaryWorld,
            int ax,
            int ay,
            int az,
            int bx,
            int by,
            int bz,
            ref int minAlong,
            ref int maxAlong,
            ref int minY,
            ref int maxY)
        {
            int da = side is NavPortalSide.West or NavPortalSide.East ? ax - boundaryWorld : az - boundaryWorld;
            int db = side is NavPortalSide.West or NavPortalSide.East ? bx - boundaryWorld : bz - boundaryWorld;
            if (da == 0 || db == 0 || (da > 0 && db > 0) || (da < 0 && db < 0))
            {
                return false;
            }

            Int128 denom = (Int128)da - db;
            if (denom == 0)
            {
                return false;
            }

            Int128 alongA = side is NavPortalSide.West or NavPortalSide.East ? az : ax;
            Int128 alongB = side is NavPortalSide.West or NavPortalSide.East ? bz : bx;
            Int128 alongNumerator = (alongA * denom) + ((alongB - alongA) * da);
            Int128 yNumerator = ((Int128)ay * denom) + (((Int128)by - ay) * da);
            Int128 along = DivideRoundHalfAwayFromZero(alongNumerator, denom);
            Int128 y = DivideRoundHalfAwayFromZero(yNumerator, denom);
            if (along < int.MinValue || along > int.MaxValue || y < int.MinValue || y > int.MaxValue)
            {
                throw new OverflowException("Recast border plane crossing overflows int centimetres.");
            }

            int alongI = (int)along;
            int yI = (int)y;
            AccumulatePlanePoint(alongI, yI, ref minAlong, ref maxAlong, ref minY, ref maxY);
            return true;
        }

        private static void AccumulatePlanePoint(
            int along,
            int y,
            ref int minAlong,
            ref int maxAlong,
            ref int yAtMinAlong,
            ref int yAtMaxAlong)
        {
            if (along < minAlong)
            {
                minAlong = along;
                yAtMinAlong = y;
            }
            else if (along == minAlong && y < yAtMinAlong)
            {
                yAtMinAlong = y;
            }

            if (along > maxAlong)
            {
                maxAlong = along;
                yAtMaxAlong = y;
            }
            else if (along == maxAlong && y > yAtMaxAlong)
            {
                yAtMaxAlong = y;
            }
        }

        private static Int128 DivideRoundHalfAwayFromZero(Int128 numerator, Int128 denominator)
        {
            if (denominator == 0)
            {
                throw new DivideByZeroException();
            }

            if (denominator < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            Int128 quotient = numerator / denominator;
            Int128 remainder = numerator % denominator;
            Int128 absRemainder = remainder < 0 ? -remainder : remainder;
            if (absRemainder * 2 >= denominator)
            {
                quotient += numerator < 0 ? -1 : 1;
            }

            return quotient;
        }

        private static bool IsInNeighborHalfSpace(NavPortalSide side, int boundaryWorld, int x, int z)
        {
            return side switch
            {
                NavPortalSide.West => x < boundaryWorld,
                NavPortalSide.East => x > boundaryWorld,
                NavPortalSide.North => z < boundaryWorld,
                NavPortalSide.South => z > boundaryWorld,
                _ => false
            };
        }

        private static int Min3(int a, int b, int c)
        {
            int m = a;
            if (b < m) m = b;
            if (c < m) m = c;
            return m;
        }

        private static int Max3(int a, int b, int c)
        {
            int m = a;
            if (b > m) m = b;
            if (c > m) m = c;
            return m;
        }

        private static void GetAlong(NavPortalSide side, int ax, int az, int bx, int bz, out int minAlong, out int maxAlong)
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

        private static bool TryClassifyBorder(
            int ax, int az, int bx, int bz,
            int tileWidthCm, int tileHeightCm,
            int borderBandCm,
            out NavPortalSide side,
            out short u0, out short v0, out short u1, out short v1)
        {
            // Recast walkable erosion pulls open edges inward. Classify with the same border band
            // derived from Recast cell/border sizing. Portal evidence still requires the exact world plane.
            int band = borderBandCm < 2 ? 2 : borderBandCm;
            const int outerTol = 2;

            if (IsInsideBorderBand(ax, 0, band, outerTol) && IsInsideBorderBand(bx, 0, band, outerTol))
            {
                side = NavPortalSide.West;
                u0 = 0; u1 = 0;
                v0 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(az, "RecastNavTileBaker.TryClassifyBorder.West.v0");
                v1 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(bz, "RecastNavTileBaker.TryClassifyBorder.West.v1");
                return true;
            }

            if (IsInsideBorderBand(ax, tileWidthCm, band, outerTol) && IsInsideBorderBand(bx, tileWidthCm, band, outerTol))
            {
                side = NavPortalSide.East;
                u0 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(tileWidthCm, "RecastNavTileBaker.TryClassifyBorder.East.u0");
                u1 = u0;
                v0 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(az, "RecastNavTileBaker.TryClassifyBorder.East.v0");
                v1 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(bz, "RecastNavTileBaker.TryClassifyBorder.East.v1");
                return true;
            }

            if (IsInsideBorderBand(az, 0, band, outerTol) && IsInsideBorderBand(bz, 0, band, outerTol))
            {
                side = NavPortalSide.North;
                v0 = 0; v1 = 0;
                u0 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(ax, "RecastNavTileBaker.TryClassifyBorder.North.u0");
                u1 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(bx, "RecastNavTileBaker.TryClassifyBorder.North.u1");
                return true;
            }

            if (IsInsideBorderBand(az, tileHeightCm, band, outerTol) && IsInsideBorderBand(bz, tileHeightCm, band, outerTol))
            {
                side = NavPortalSide.South;
                v0 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(tileHeightCm, "RecastNavTileBaker.TryClassifyBorder.South.v0");
                v1 = v0;
                u0 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(ax, "RecastNavTileBaker.TryClassifyBorder.South.u0");
                u1 = NavBorderPortalCoordinateContract.RequirePortalCoordinate(bx, "RecastNavTileBaker.TryClassifyBorder.South.u1");
                return true;
            }

            side = default;
            u0 = v0 = u1 = v1 = 0;
            return false;
        }

        private static int ComputeRecastBorderBandCm(int agentRadiusCm, int rasterCellSizeCm)
        {
            // Mirror BuildRcConfig cell/border derivation so eroded open edges remain classifiable.
            if (rasterCellSizeCm <= 0)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.recast.rasterCellSizeCm must be > 0.");
            }

            float radiusM = agentRadiusCm / 100f;
            float cellSizeM = rasterCellSizeCm / 100f;
            int borderCells = RcConfig.CalcBorder(radiusM, cellSizeM);
            int bandCm = checked((int)MathF.Ceiling((borderCells + 2) * cellSizeM * 100f));
            return bandCm < 2 ? 2 : bandCm;
        }

        private static bool IsInsideBorderBand(int value, int boundary, int inwardBand, int outerTol)
        {
            int min = boundary - inwardBand;
            int max = boundary + outerTol;
            return value >= min && value <= max;
        }

        private static int ComparePortals(NavBorderPortal a, NavBorderPortal b)
        {
            int side = ((byte)a.Side).CompareTo((byte)b.Side);
            if (side != 0) return side;
            int u0 = a.U0.CompareTo(b.U0);
            if (u0 != 0) return u0;
            int v0 = a.V0.CompareTo(b.V0);
            if (v0 != 0) return v0;
            int u1 = a.U1.CompareTo(b.U1);
            if (u1 != 0) return u1;
            int v1 = a.V1.CompareTo(b.V1);
            if (v1 != 0) return v1;
            int ly = a.LeftYcm.CompareTo(b.LeftYcm);
            if (ly != 0) return ly;
            return a.RightYcm.CompareTo(b.RightYcm);
        }

        private static byte ResolveAreaIdFromSurface(
            NavTriangleSurfaceSnapshot surface,
            ReadOnlySpan<int> triangleIndices,
            int worldXcm,
            int worldZcm)
        {
            ReadOnlySpan<int> vx = surface.VertexXcm;
            ReadOnlySpan<int> vz = surface.VertexZcm;
            ReadOnlySpan<int> ta = surface.TriA;
            ReadOnlySpan<int> tb = surface.TriB;
            ReadOnlySpan<int> tc = surface.TriC;
            ReadOnlySpan<byte> areas = surface.TriAreaIds;
            ReadOnlySpan<NavTriangleSurfaceFlags> flags = surface.TriFlags;

            for (int i = 0; i < triangleIndices.Length; i++)
            {
                int tri = triangleIndices[i];
                if ((flags[tri] & NavTriangleSurfaceFlags.WalkCandidate) == 0)
                {
                    continue;
                }

                int a = ta[tri];
                int b = tb[tri];
                int c = tc[tri];
                if (PointInTriangle2D(
                        worldXcm, worldZcm,
                        vx[a], vz[a],
                        vx[b], vz[b],
                        vx[c], vz[c]))
                {
                    return areas[tri];
                }
            }

            return 0;
        }

        private static bool PointInTriangle2D(
            float px, float pz,
            float ax, float az,
            float bx, float bz,
            float cx, float cz)
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
            int originXcm,
            int originZcm,
            Dictionary<(int X, int Y, int Z), int> vertexIndex,
            List<int> vx,
            List<int> vy,
            List<int> vz)
        {
            int vi = detailVertexIndex * 3;
            int worldXcm = (int)MathF.Round(detail.verts[vi + 0] * 100f);
            int worldYcm = (int)MathF.Round(detail.verts[vi + 1] * 100f);
            int worldZcm = (int)MathF.Round(detail.verts[vi + 2] * 100f);
            int localXcm = worldXcm - originXcm;
            int localZcm = worldZcm - originZcm;
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
                if (a < b) { A = a; B = b; }
                else { A = b; B = a; }
            }

            public bool Equals(EdgeKey other) => A == other.A && B == other.B;
            public override bool Equals(object? obj) => obj is EdgeKey other && Equals(other);
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

        private static void BuildAdjacency(List<int> triA, List<int> triB, List<int> triC, int[] n0, int[] n1, int[] n2)
        {
            var map = new Dictionary<EdgeKey, EdgeRef>(triA.Count * 2);
            for (int t = 0; t < triA.Count; t++)
            {
                AddEdge(map, n0, n1, n2, t, 0, triA[t], triB[t]);
                AddEdge(map, n0, n1, n2, t, 1, triB[t], triC[t]);
                AddEdge(map, n0, n1, n2, t, 2, triC[t], triA[t]);
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

        private static int RequireExactPositiveIntCm(float value, string owner)
        {
            int cm = RequireExactIntCm(value, owner);
            if (cm <= 0)
            {
                throw new InvalidOperationException($"{owner} must be an exact positive integer centimeter value.");
            }

            return cm;
        }

        private static int RequireExactNonNegativeIntCm(float value, string owner)
        {
            int cm = RequireExactIntCm(value, owner);
            if (cm < 0)
            {
                throw new InvalidOperationException($"{owner} must be an exact nonnegative integer centimeter value.");
            }

            return cm;
        }

        private static int RequireExactIntCm(float value, string owner)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new InvalidOperationException($"{owner} must be a finite number.");
            }

            int cm = (int)value;
            if ((float)cm != value)
            {
                throw new InvalidOperationException(
                    $"{owner} must be an exact integer centimeter value for Recast triangle-surface bake; got {value}.");
            }

            return cm;
        }
    }
}
