using System;
using System.IO;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Production layered-span adapter over the existing kernel chain and common <see cref="NavTile"/> model.
    /// Declares triangle-surface offline + runtime-incremental capabilities only.
    /// </summary>
    public sealed class LayeredSpanNavBakeAlgorithm : INavBakeAlgorithm
    {
        private readonly NavLayeredSpanConfig _config;
        private readonly LayeredSpanScratchPool _pool;

        public LayeredSpanNavBakeAlgorithm(NavLayeredSpanConfig config, LayeredSpanScratchPool pool)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _config.Validate();
            if (!ReferenceEquals(pool.Config, config) &&
                !ConfigsEqual(pool.Config, config))
            {
                throw new InvalidOperationException(
                    "LayeredSpanNavBakeAlgorithm requires the scratch pool to own the same layeredSpan config capacities.");
            }
        }

        public LayeredSpanNavBakeAlgorithm(LayeredSpanScratchPool pool)
            : this(pool?.Config ?? throw new ArgumentNullException(nameof(pool)), pool)
        {
        }

        public NavBakeAlgorithmKind Kind => NavBakeAlgorithmKind.LayeredSpan;

        /// <summary>
        /// Exact preallocated scratch channel payload bytes owned by the pool wired to this algorithm.
        /// </summary>
        public long PreallocatedScratchChannelPayloadBytes => _pool.PreallocatedChannelPayloadBytes;

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

        // Repeated-bake N>=3 byte-identical serialized-tile contract proves bitwise determinism
        // for offline bakes through the production adapter (LayeredSpanBitwiseDeterminismContractTests).
        public bool GuaranteesBitwiseDeterminism => true;

        public bool Supports3DMultiLayer => true;

        /// <summary>
        /// True for the banked <see cref="TryBakeInto"/> hot path only; legacy <see cref="TryBake"/> allocates.
        /// </summary>
        public bool IsZeroAllocationHotPath => true;

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
            NavTile destination = NavTile.CreateBanked(
                Math.Max(1, _config.TriangulationVertexCapacity),
                Math.Max(1, _config.TriangulationTriangleCapacity),
                Math.Max(1, _config.BorderPortalCapacity));
            byte[] scratch = new byte[NavTileBinary.GetSerializedSize(
                CreateFullProbe(
                    destination.VertexCapacity,
                    destination.TriangleCapacity,
                    destination.PortalCapacity))];
            bool success = TryBakeInto(
                context,
                target,
                layer,
                navProfile,
                agentProfile,
                destination,
                scratch,
                out artifact);
            tile = destination;
            detourTileBytes = Array.Empty<byte>();
            return success;
        }

        public bool TryBakeInto(
            NavBakeContext context,
            NavBakeTileCoord target,
            NavLayerConfig layer,
            NavMeshAgentProfileConfig navProfile,
            AgentProfileConfig agentProfile,
            NavTile destination,
            Span<byte> checksumScratch,
            out NavBakeArtifact artifact)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (navProfile == null) throw new ArgumentNullException(nameof(navProfile));
            if (agentProfile == null) throw new ArgumentNullException(nameof(agentProfile));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            if (context.InputKind != NavBakeInputKind.TriangleSurface)
            {
                throw new InvalidOperationException(
                    "LayeredSpanNavBakeAlgorithm supports triangle-surface input only " +
                    $"(requested {NavBakeAdapterCapability.FormatInputKind(context.InputKind)}).");
            }

            NavTriangleSurfaceTileIndex surfaceIndex = context.RequireTriangleSurface();
            NavTriangleSurfaceTileGrid tileGrid = surfaceIndex.Grid;
            NavTriangleSurfaceSnapshot surface = surfaceIndex.Surface;

            int agentHeightCm = RequireExactPositiveIntCm(agentProfile.HeightCm, "AgentProfile.heightCm");
            int agentRadiusCm = RequireExactNonNegativeIntCm(agentProfile.RadiusCm, "AgentProfile.radiusCm");
            int minWalkableUpDotQ1M = LayeredSpanSlopeQ1M.CompileMinWalkableUpDotQ1M(
                navProfile.MaxSlopeDeg,
                "NavMeshBakeConfig.profiles.maxSlopeDeg");

            if (navProfile.MaxClimbCm < 0)
            {
                throw new InvalidOperationException(
                    "NavMeshBakeConfig.profiles.maxClimbCm must be >= 0.");
            }

            int expectedHaloPaddingCm = checked(_config.RasterHaloCells * _config.RasterCellSizeCm);
            if (tileGrid.HaloPaddingCm < expectedHaloPaddingCm)
            {
                throw new InvalidOperationException(
                    "NavTriangleSurfaceTileGrid.haloPaddingCm must be >= " +
                    "NavMeshBakeConfig.layeredSpan.rasterHaloCells * rasterCellSizeCm " +
                    $"(grid={tileGrid.HaloPaddingCm}, requiredMinimum={expectedHaloPaddingCm}).");
            }

            DeriveTargetRaster(
                tileGrid,
                target,
                _config.RasterCellSizeCm,
                _config.RasterHaloCells,
                out int originXcm,
                out int originZcm,
                out int targetMinXcm,
                out int targetMinZcm,
                out int targetMaxXcm,
                out int targetMaxZcm,
                out int columnCountX,
                out int columnCountZ);

            var rasterGrid = new LayeredSpanRasterGridSpec(
                originXcm,
                originZcm,
                _config.RasterCellSizeCm,
                columnCountX,
                columnCountZ);

            var walkSpec = new LayeredSpanWalkabilitySpec(
                agentHeightCm,
                minWalkableUpDotQ1M,
                _config.SameSurfaceToleranceCm);
            var linkSpec = new LayeredSpanWalkLinkSpec(navProfile.MaxClimbCm);
            var contourSpec = new LayeredSpanContourSpec(
                _config.MaxSimplificationErrorCm,
                targetMinXcm,
                targetMinZcm,
                targetMaxXcm,
                targetMaxZcm);
            var triSpec = new LayeredSpanTriangulationSpec(
                _config.ParsedHeightRounding,
                _config.MaxLawsonFlipCount,
                targetMinXcm,
                targetMinZcm,
                targetMaxXcm,
                targetMaxZcm,
                _config.RasterCellSizeCm,
                _config.RasterCellSizeCm);

            ulong buildConfigHash = ComputeBuildConfigHash(
                context.BuildConfig,
                _config,
                navProfile.MaxClimbCm,
                minWalkableUpDotQ1M,
                agentHeightCm,
                agentRadiusCm,
                layer.Layer);

            ReadOnlySpan<int> triangleIndices = surfaceIndex.GetTriangleIndices(target);
            var tileId = new NavTileId(target.ChunkX, target.ChunkY, layer.Layer);

            if (_config.ColumnCapacity < rasterGrid.ColumnCount)
            {
                throw new InvalidOperationException(
                    $"LayeredSpanScratch.columnCapacity ({_config.ColumnCapacity}); required {rasterGrid.ColumnCount}.");
            }

            // Obstacle-free uniform flat floor → Editor Bridge flat-grid-baseline-v2 NavTile
            // (DefaultGridNavTileFactory). Slopes / holes / stacked floors / tile-local obstacles
            // keep the dense LayeredSpan pipeline below.
            if (TryFillFlatGridBaselineFromSurface(
                    surface,
                    triangleIndices,
                    context.Obstacles,
                    layer.Id,
                    targetMinXcm,
                    targetMinZcm,
                    targetMaxXcm,
                    targetMaxZcm,
                    _config.RasterCellSizeCm,
                    tileGrid.TileWidthCm,
                    tileGrid.TileHeightCm,
                    destination,
                    tileId,
                    context.TileVersion,
                    buildConfigHash,
                    checksumScratch,
                    out artifact))
            {
                return true;
            }

            LayeredSpanScratchSlot slot = _pool.Acquire();
            try
            {
                LayeredSpanRasterizer.Rasterize(surface, triangleIndices, in rasterGrid, slot.Raw);
                LayeredSpanWalkabilityClassifier.Classify(slot.Raw, in walkSpec, slot.Walkability);
                LayeredSpanObstacleOverlayBuilder.Apply(
                    slot.Raw,
                    slot.Walkability,
                    in rasterGrid,
                    context.Obstacles,
                    layer.Id,
                    agentHeightCm);
                LayeredSpanSurfaceSheetAssigner.Assign(surface, slot.Raw, in rasterGrid, in walkSpec, slot.Sheets);
                LayeredSpanWalkLinkBuilder.Build(slot.Raw, slot.Walkability, in rasterGrid, in linkSpec, slot.Links);
                LayeredSpanRadiusFieldBuilder.Build(
                    slot.Raw, slot.Walkability, slot.Sheets, slot.Links, in rasterGrid, slot.Radius);
                LayeredSpanRegionBuilder.Build(
                    slot.Raw,
                    slot.Walkability,
                    slot.Sheets,
                    slot.Links,
                    slot.Radius,
                    agentRadiusCm,
                    slot.Regions);
                LayeredSpanContourBuilder.Build(
                    slot.Raw,
                    slot.Walkability,
                    slot.Sheets,
                    slot.Links,
                    slot.Radius,
                    slot.Regions,
                    in rasterGrid,
                    in contourSpec,
                    slot.Contours);
                LayeredSpanTriangulationBuilder.Build(
                    surface,
                    slot.Raw,
                    slot.Walkability,
                    slot.Sheets,
                    slot.Links,
                    slot.Radius,
                    slot.Regions,
                    slot.Contours,
                    in rasterGrid,
                    in triSpec,
                    slot.Triangulation);

                if (slot.Triangulation.TriangleCount == 0)
                {
                    NavValidEmptyTile.Fill(
                        destination,
                        tileId,
                        context.TileVersion,
                        buildConfigHash,
                        targetMinXcm,
                        targetMinZcm,
                        checksumScratch);
                    artifact = NavValidEmptyTile.CreateSuccessArtifact(destination);
                    return true;
                }

                FillNavTileFromTriangulation(
                    destination,
                    slot.Triangulation,
                    tileId,
                    context.TileVersion,
                    buildConfigHash,
                    targetMinXcm,
                    targetMinZcm,
                    agentRadiusCm,
                    checksumScratch);
                artifact = new NavBakeArtifact(
                    destination.TileId,
                    destination.TileVersion,
                    NavBakeStage.Serialize,
                    NavBakeErrorCode.None,
                    message: string.Empty,
                    walkableTriangleCount: destination.TriangleCount,
                    vertexCount: destination.VertexCount,
                    triangleCount: destination.TriangleCount,
                    portalCount: destination.PortalCount);
                return true;
            }
            finally
            {
                _pool.Release(slot);
            }
        }

        internal static void DeriveTargetRaster(
            NavTriangleSurfaceTileGrid tileGrid,
            NavBakeTileCoord target,
            int cellSizeCm,
            int haloCells,
            out int rasterOriginXcm,
            out int rasterOriginZcm,
            out int targetMinXcm,
            out int targetMinZcm,
            out int targetMaxXcm,
            out int targetMaxZcm,
            out int columnCountX,
            out int columnCountZ)
        {
            if (cellSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeCm), cellSizeCm, "rasterCellSizeCm must be > 0.");
            }

            if (haloCells < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(haloCells), haloCells, "rasterHaloCells must be >= 0.");
            }

            if (tileGrid.TileWidthCm % cellSizeCm != 0)
            {
                throw new InvalidOperationException(
                    "NavTriangleSurfaceTileGrid.tileWidthCm must be an exact multiple of layeredSpan.rasterCellSizeCm " +
                    $"(tileWidthCm={tileGrid.TileWidthCm}, rasterCellSizeCm={cellSizeCm}).");
            }

            if (tileGrid.TileHeightCm % cellSizeCm != 0)
            {
                throw new InvalidOperationException(
                    "NavTriangleSurfaceTileGrid.tileHeightCm must be an exact multiple of layeredSpan.rasterCellSizeCm " +
                    $"(tileHeightCm={tileGrid.TileHeightCm}, rasterCellSizeCm={cellSizeCm}).");
            }

            targetMinXcm = checked(tileGrid.OriginXcm + checked(target.ChunkX * tileGrid.TileWidthCm));
            targetMinZcm = checked(tileGrid.OriginZcm + checked(target.ChunkY * tileGrid.TileHeightCm));
            targetMaxXcm = checked(targetMinXcm + tileGrid.TileWidthCm);
            targetMaxZcm = checked(targetMinZcm + tileGrid.TileHeightCm);

            if ((targetMinXcm - tileGrid.OriginXcm) % cellSizeCm != 0 ||
                (targetMinZcm - tileGrid.OriginZcm) % cellSizeCm != 0)
            {
                throw new InvalidOperationException(
                    "Target tile origin is not aligned to layeredSpan.rasterCellSizeCm " +
                    $"(targetMin=({targetMinXcm},{targetMinZcm}), gridOrigin=({tileGrid.OriginXcm},{tileGrid.OriginZcm}), cell={cellSizeCm}).");
            }

            int targetCellsX = tileGrid.TileWidthCm / cellSizeCm;
            int targetCellsZ = tileGrid.TileHeightCm / cellSizeCm;
            columnCountX = checked(targetCellsX + checked(2 * haloCells));
            columnCountZ = checked(targetCellsZ + checked(2 * haloCells));
            int haloPadCm = checked(haloCells * cellSizeCm);
            rasterOriginXcm = checked(targetMinXcm - haloPadCm);
            rasterOriginZcm = checked(targetMinZcm - haloPadCm);
        }

        private static bool TryFillFlatGridBaselineFromSurface(
            NavTriangleSurfaceSnapshot surface,
            ReadOnlySpan<int> triangleIndices,
            INavObstacleSource? obstacles,
            string layerId,
            int targetMinXcm,
            int targetMinZcm,
            int targetMaxXcm,
            int targetMaxZcm,
            int cellSizeCm,
            int tileWidthCm,
            int tileHeightCm,
            NavTile destination,
            NavTileId tileId,
            uint tileVersion,
            ulong buildConfigHash,
            Span<byte> checksumScratch,
            out NavBakeArtifact artifact)
        {
            artifact = default;
            if (cellSizeCm <= 0 ||
                tileWidthCm % cellSizeCm != 0 ||
                tileHeightCm % cellSizeCm != 0 ||
                triangleIndices.Length == 0)
            {
                return false;
            }

            if (!TryGetUniformFlatFloorY(surface, triangleIndices, out int floorYcm))
            {
                return false;
            }

            if (TileHasOverlappingObstacle(
                    obstacles,
                    layerId,
                    targetMinXcm,
                    targetMinZcm,
                    targetMaxXcm,
                    targetMaxZcm))
            {
                return false;
            }

            int targetCellsX = tileWidthCm / cellSizeCm;
            int targetCellsZ = tileHeightCm / cellSizeCm;
            DefaultGridNavTileFactory.FillFlatTile(
                destination,
                tileId.ChunkX,
                tileId.ChunkY,
                tileId.Layer,
                tileVersion,
                buildConfigHash,
                targetMinXcm,
                targetMinZcm,
                tileWidthCm,
                tileHeightCm,
                targetCellsX,
                targetCellsZ,
                floorYcm);

            NavTileBinary.AssignChecksum(destination, checksumScratch);
            artifact = new NavBakeArtifact(
                destination.TileId,
                destination.TileVersion,
                NavBakeStage.Serialize,
                NavBakeErrorCode.None,
                message: DefaultGridNavTileFactory.SourceId,
                walkableTriangleCount: destination.TriangleCount,
                vertexCount: destination.VertexCount,
                triangleCount: destination.TriangleCount,
                portalCount: destination.PortalCount);
            return true;
        }

        private static bool TryGetUniformFlatFloorY(
            NavTriangleSurfaceSnapshot surface,
            ReadOnlySpan<int> triangleIndices,
            out int floorYcm)
        {
            floorYcm = 0;
            bool hasY = false;
            ReadOnlySpan<int> vx = surface.VertexXcm;
            ReadOnlySpan<int> vy = surface.VertexYcm;
            ReadOnlySpan<int> vz = surface.VertexZcm;
            ReadOnlySpan<int> triA = surface.TriA;
            ReadOnlySpan<int> triB = surface.TriB;
            ReadOnlySpan<int> triC = surface.TriC;
            for (int i = 0; i < triangleIndices.Length; i++)
            {
                int tri = triangleIndices[i];
                int a = triA[tri];
                int b = triB[tri];
                int c = triC[tri];
                int ya = vy[a];
                int yb = vy[b];
                int yc = vy[c];
                if (ya != yb || yb != yc)
                {
                    return false;
                }

                // Reject non-horizontal floors (XZ-degenerate / vertical walls).
                long e1x = vx[b] - vx[a];
                long e1z = vz[b] - vz[a];
                long e2x = vx[c] - vx[a];
                long e2z = vz[c] - vz[a];
                if ((e1x * e2z) - (e1z * e2x) == 0)
                {
                    return false;
                }

                if (!hasY)
                {
                    floorYcm = ya;
                    hasY = true;
                }
                else if (ya != floorYcm)
                {
                    return false;
                }
            }

            return hasY;
        }

        private static bool TileHasOverlappingObstacle(
            INavObstacleSource? obstacles,
            string layerId,
            int targetMinXcm,
            int targetMinZcm,
            int targetMaxXcm,
            int targetMaxZcm)
        {
            if (obstacles == null)
            {
                return false;
            }

            int count = obstacles.ObstacleCount;
            for (int i = 0; i < count; i++)
            {
                if (!obstacles.IsEnabled(i) || !obstacles.MatchesLayer(i, layerId))
                {
                    continue;
                }

                switch (obstacles.GetKind(i))
                {
                    case NavObstacleKind.Circle:
                    {
                        obstacles.GetCircle(i, out int centerX, out int centerZ, out int radiusCm);
                        if (CircleOverlapsClosedAabb(
                                centerX,
                                centerZ,
                                radiusCm,
                                targetMinXcm,
                                targetMinZcm,
                                targetMaxXcm,
                                targetMaxZcm))
                        {
                            return true;
                        }

                        break;
                    }
                    case NavObstacleKind.Polygon:
                    {
                        int vertexCount = obstacles.GetPolygonVertexCount(i);
                        if (vertexCount <= 0)
                        {
                            return true;
                        }

                        obstacles.GetPolygonVertex(i, 0, out int minX, out int minZ);
                        int maxX = minX;
                        int maxZ = minZ;
                        for (int v = 1; v < vertexCount; v++)
                        {
                            obstacles.GetPolygonVertex(i, v, out int x, out int z);
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (z < minZ) minZ = z;
                            if (z > maxZ) maxZ = z;
                        }

                        if (AabbOverlapsClosedAabb(
                                minX,
                                minZ,
                                maxX,
                                maxZ,
                                targetMinXcm,
                                targetMinZcm,
                                targetMaxXcm,
                                targetMaxZcm))
                        {
                            return true;
                        }

                        break;
                    }
                    default:
                        // Unknown kinds force the dense bake path — never silently treat as open flat.
                        return true;
                }
            }

            return false;
        }

        private static bool CircleOverlapsClosedAabb(
            int centerX,
            int centerZ,
            int radiusCm,
            int minX,
            int minZ,
            int maxX,
            int maxZ)
        {
            int clampedX = centerX < minX ? minX : (centerX > maxX ? maxX : centerX);
            int clampedZ = centerZ < minZ ? minZ : (centerZ > maxZ ? maxZ : centerZ);
            long dx = centerX - clampedX;
            long dz = centerZ - clampedZ;
            long r = radiusCm;
            return (dx * dx) + (dz * dz) <= (r * r);
        }

        private static bool AabbOverlapsClosedAabb(
            int aMinX,
            int aMinZ,
            int aMaxX,
            int aMaxZ,
            int bMinX,
            int bMinZ,
            int bMaxX,
            int bMaxZ)
            => aMinX <= bMaxX && aMaxX >= bMinX && aMinZ <= bMaxZ && aMaxZ >= bMinZ;

        private static void FillNavTileFromTriangulation(
            NavTile destination,
            LayeredSpanTriangulationScratch triangulation,
            NavTileId tileId,
            uint tileVersion,
            ulong buildConfigHash,
            int originXcm,
            int originZcm,
            int agentRadiusCm,
            Span<byte> checksumScratch)
        {
            int vertexCount = triangulation.VertexCount;
            int triangleCount = triangulation.TriangleCount;
            int portalCount = triangulation.PortalCount;

            if (vertexCount > destination.VertexCapacity)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.outputVertexCapacity ({destination.VertexCapacity}) exhausted; required {vertexCount}.");
            }

            if (triangleCount > destination.TriangleCapacity)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.outputTriangleCapacity ({destination.TriangleCapacity}) exhausted; required {triangleCount}.");
            }

            ReadOnlySpan<int> srcX = triangulation.VertexXcm;
            ReadOnlySpan<int> srcY = triangulation.VertexYcm;
            ReadOnlySpan<int> srcZ = triangulation.VertexZcm;
            for (int i = 0; i < vertexCount; i++)
            {
                destination.VertexXcm[i] = checked(srcX[i] - originXcm);
                destination.VertexYcm[i] = srcY[i];
                destination.VertexZcm[i] = checked(srcZ[i] - originZcm);
            }

            ReadOnlySpan<int> srcA = triangulation.TriA;
            ReadOnlySpan<int> srcB = triangulation.TriB;
            ReadOnlySpan<int> srcC = triangulation.TriC;
            ReadOnlySpan<int> srcN0 = triangulation.N0;
            ReadOnlySpan<int> srcN1 = triangulation.N1;
            ReadOnlySpan<int> srcN2 = triangulation.N2;
            ReadOnlySpan<byte> srcArea = triangulation.TriAreaIds;
            for (int i = 0; i < triangleCount; i++)
            {
                destination.TriA[i] = srcA[i];
                destination.TriB[i] = srcB[i];
                destination.TriC[i] = srcC[i];
                destination.N0[i] = srcN0[i];
                destination.N1[i] = srcN1[i];
                destination.N2[i] = srcN2[i];
                destination.TriAreaIds[i] = srcArea[i];
            }

            int acceptedPortals = 0;
            ReadOnlySpan<int> portalClearance = triangulation.PortalClearanceCm;
            for (int i = 0; i < portalCount; i++)
            {
                if (portalClearance[i] >= agentRadiusCm)
                {
                    acceptedPortals++;
                }
            }

            if (acceptedPortals > destination.PortalCapacity)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.runtimeIncremental.outputPortalCapacity ({destination.PortalCapacity}) exhausted; required {acceptedPortals}.");
            }

            int portalCursor = 0;
            ReadOnlySpan<NavPortalSide> sides = triangulation.PortalSides;
            ReadOnlySpan<int> leftX = triangulation.PortalLeftXcm;
            ReadOnlySpan<int> leftY = triangulation.PortalLeftYcm;
            ReadOnlySpan<int> leftZ = triangulation.PortalLeftZcm;
            ReadOnlySpan<int> rightX = triangulation.PortalRightXcm;
            ReadOnlySpan<int> rightY = triangulation.PortalRightYcm;
            ReadOnlySpan<int> rightZ = triangulation.PortalRightZcm;
            for (int i = 0; i < portalCount; i++)
            {
                if (portalClearance[i] < agentRadiusCm)
                {
                    continue;
                }

                int localLeftX = checked(leftX[i] - originXcm);
                int localLeftZ = checked(leftZ[i] - originZcm);
                int localRightX = checked(rightX[i] - originXcm);
                int localRightZ = checked(rightZ[i] - originZcm);
                destination.Portals[portalCursor++] = new NavBorderPortal(
                    sides[i],
                    NavBorderPortalCoordinateContract.RequirePortalCoordinate(localLeftX, "LayeredSpan.portal.u0"),
                    NavBorderPortalCoordinateContract.RequirePortalCoordinate(localLeftZ, "LayeredSpan.portal.v0"),
                    NavBorderPortalCoordinateContract.RequirePortalCoordinate(localRightX, "LayeredSpan.portal.u1"),
                    NavBorderPortalCoordinateContract.RequirePortalCoordinate(localRightZ, "LayeredSpan.portal.v1"),
                    localLeftX,
                    leftY[i],
                    localLeftZ,
                    localRightX,
                    rightY[i],
                    localRightZ,
                    portalClearance[i]);
            }

            destination.AssignHeader(tileId, tileVersion, buildConfigHash, originXcm, originZcm);
            destination.SetCounts(vertexCount, triangleCount, acceptedPortals);
            NavTileBinary.AssignChecksum(destination, checksumScratch);
        }

        private static NavTile CreateFullProbe(int vertexCapacity, int triangleCapacity, int portalCapacity)
        {
            NavTile probe = NavTile.CreateBanked(vertexCapacity, triangleCapacity, portalCapacity);
            probe.SetCounts(vertexCapacity, triangleCapacity, portalCapacity);
            return probe;
        }

        private static ulong ComputeBuildConfigHash(
            NavBuildConfig buildConfig,
            NavLayeredSpanConfig layered,
            int maxClimbCm,
            int minWalkableUpDotQ1M,
            int agentHeightCm,
            int agentRadiusCm,
            int layer)
        {
            ulong h = buildConfig.ComputeHash();
            h = Mix(h, layered.RasterCellSizeCm);
            h = Mix(h, layered.RasterHaloCells);
            h = Mix(h, layered.SameSurfaceToleranceCm);
            h = Mix(h, layered.MaxSimplificationErrorCm);
            h = Mix(h, (int)layered.ParsedHeightRounding);
            h = Mix(h, layered.MaxLawsonFlipCount);
            h = Mix(h, layered.ScratchSlotCount);
            h = Mix(h, layered.ColumnCapacity);
            h = Mix(h, layered.SpanCapacity);
            h = Mix(h, layered.ClassifiedSpanCapacity);
            h = Mix(h, layered.WalkableSpanCapacity);
            h = Mix(h, layered.LinkCapacity);
            h = Mix(h, layered.SheetCapacity);
            h = Mix(h, layered.PortalIntervalCapacity);
            h = Mix(h, layered.RegionCapacity);
            h = Mix(h, layered.ChartCapacity);
            h = Mix(h, layered.RingCapacity);
            h = Mix(h, layered.ContourVertexCapacity);
            h = Mix(h, layered.ContourEdgeCapacity);
            h = Mix(h, layered.SeamCapacity);
            h = Mix(h, layered.CanonicalLinkCapacity);
            h = Mix(h, layered.SplitPointCapacity);
            h = Mix(h, layered.TriangulationVertexCapacity);
            h = Mix(h, layered.TriangulationTriangleCapacity);
            h = Mix(h, layered.ConstrainedEdgeCapacity);
            h = Mix(h, layered.BorderPortalCapacity);
            h = Mix(h, layered.PolygonVertexCapacity);
            h = Mix(h, layered.AdjacencyEdgeCapacity);
            h = Mix(h, layered.BridgeCandidateCapacity);
            h = Mix(h, layered.RingWorkCapacity);
            h = Mix(h, layered.TemporaryConstraintFlagCapacity);
            h = Mix(h, maxClimbCm);
            h = Mix(h, minWalkableUpDotQ1M);
            h = Mix(h, agentHeightCm);
            h = Mix(h, agentRadiusCm);
            h = Mix(h, layer);
            return h;
        }

        private static ulong Mix(ulong hash, int value)
            => (hash ^ (ulong)(uint)value) * 1099511628211UL;

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
                    $"{owner} must be an exact integer centimeter value for layered-span; got {value}.");
            }

            return cm;
        }

        private static bool ConfigsEqual(NavLayeredSpanConfig a, NavLayeredSpanConfig b)
        {
            return a.ScratchSlotCount == b.ScratchSlotCount &&
                   a.RasterCellSizeCm == b.RasterCellSizeCm &&
                   a.RasterHaloCells == b.RasterHaloCells &&
                   a.SameSurfaceToleranceCm == b.SameSurfaceToleranceCm &&
                   a.MaxSimplificationErrorCm == b.MaxSimplificationErrorCm &&
                   string.Equals(a.HeightRounding, b.HeightRounding, StringComparison.Ordinal) &&
                   a.MaxLawsonFlipCount == b.MaxLawsonFlipCount &&
                   a.ColumnCapacity == b.ColumnCapacity &&
                   a.SpanCapacity == b.SpanCapacity &&
                   a.ClassifiedSpanCapacity == b.ClassifiedSpanCapacity &&
                   a.WalkableSpanCapacity == b.WalkableSpanCapacity &&
                   a.LinkCapacity == b.LinkCapacity &&
                   a.SheetCapacity == b.SheetCapacity &&
                   a.PortalIntervalCapacity == b.PortalIntervalCapacity &&
                   a.RegionCapacity == b.RegionCapacity &&
                   a.ChartCapacity == b.ChartCapacity &&
                   a.RingCapacity == b.RingCapacity &&
                   a.ContourVertexCapacity == b.ContourVertexCapacity &&
                   a.ContourEdgeCapacity == b.ContourEdgeCapacity &&
                   a.SeamCapacity == b.SeamCapacity &&
                   a.CanonicalLinkCapacity == b.CanonicalLinkCapacity &&
                   a.SplitPointCapacity == b.SplitPointCapacity &&
                   a.TriangulationVertexCapacity == b.TriangulationVertexCapacity &&
                   a.TriangulationTriangleCapacity == b.TriangulationTriangleCapacity &&
                   a.ConstrainedEdgeCapacity == b.ConstrainedEdgeCapacity &&
                   a.BorderPortalCapacity == b.BorderPortalCapacity &&
                   a.PolygonVertexCapacity == b.PolygonVertexCapacity &&
                   a.AdjacencyEdgeCapacity == b.AdjacencyEdgeCapacity &&
                   a.BridgeCandidateCapacity == b.BridgeCandidateCapacity &&
                   a.RingWorkCapacity == b.RingWorkCapacity &&
                   a.TemporaryConstraintFlagCapacity == b.TemporaryConstraintFlagCapacity;
        }
    }
}
