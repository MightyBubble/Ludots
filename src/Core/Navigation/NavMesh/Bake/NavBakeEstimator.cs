using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public enum NavBakeBudgetStatus : byte
    {
        Ok = 0,
        Large = 1,
        Reject = 2
    }

    public sealed class NavBakeProfileEstimate
    {
        public required string ProfileId { get; init; }

        public required float AgentRadiusCm { get; init; }

        public required float AgentHeightCm { get; init; }

        public required float AgentClearanceCm { get; init; }

        public required int MaxClimbCm { get; init; }

        public required float MaxSlopeDeg { get; init; }

        public required float MinWalkableUpDot { get; init; }

        public required float RecastCellSizeCm { get; init; }

        public required float RecastCellHeightCm { get; init; }

        public required int RecastColumnsPerAxis { get; init; }

        public required int RecastColumnBudgetPerTile { get; init; }

        public required int WalkableHeightVoxels { get; init; }

        public required int WalkableClimbVoxels { get; init; }
    }

    public sealed class NavBakeEstimateReport
    {
        public required string MapId { get; init; }

        public required string SourceUri { get; init; }

        public required string Mode { get; init; }

        public required string Algorithm { get; init; }

        public required string EstimateHash { get; init; }

        public required string TerrainContentHash { get; init; }

        public required int TerrainWidthCells { get; init; }

        public required int TerrainHeightCells { get; init; }

        public required int TerrainChunkCells { get; init; }

        public required int CellCm { get; init; }

        public required int TileWorldWidthCm { get; init; }

        public required int TileWorldHeightCm { get; init; }

        public required int FullTileCountX { get; init; }

        public required int FullTileCountY { get; init; }

        public required int FullTileCount { get; init; }

        public required int TargetTileCount { get; init; }

        public required int LayerCount { get; init; }

        public required int ProfileCount { get; init; }

        public required int BakeOperationCount { get; init; }

        public required int ObstacleCount { get; init; }

        public required long TerrainCellSampleCount { get; init; }

        public required long RecastColumnBudgetTotal { get; init; }

        public required long BudgetWorkUnitCount { get; init; }

        public required long EstimatedTileBytesLow { get; init; }

        public required long EstimatedTileBytesHigh { get; init; }

        public required int EffectiveWorkers { get; init; }

        public required double EstimatedSerialSecondsLow { get; init; }

        public required double EstimatedSerialSecondsHigh { get; init; }

        public required double EstimatedSecondsLow { get; init; }

        public required double EstimatedSecondsHigh { get; init; }

        public required NavBakeBudgetStatus BudgetStatus { get; init; }

        public required string BudgetStatusText { get; init; }

        public required string BudgetMessage { get; init; }

        public required bool RequiresExplicitLargeBakeApproval { get; init; }

        public required IReadOnlyList<NavBakeProfileEstimate> Profiles { get; init; }
    }

    public static class NavBakeEstimator
    {
        public const long OkWorkUnitThreshold = 2_000_000L;
        public const long LargeWorkUnitThreshold = 200_000_000L;
        public const float SimpleMsPerOperationLow = 20f;
        public const float SimpleMsPerOperationHigh = 80f;
        public const float RecastMsPerOperationLow = 80f;
        public const float RecastMsPerOperationHigh = 250f;
        private const int BytesPerKiB = 1024;
        public const int EstimatedBytesPerOperationLow = 48 * BytesPerKiB;
        public const int EstimatedBytesPerOperationHigh = SpatialScaleDefaults.MacroTileCells * BytesPerKiB;
        public const long ExactCdtReferenceWorkUnitsPerOperation =
            (long)SpatialScaleDefaults.TerrainChunkCells * SpatialScaleDefaults.TerrainChunkCells;
        public const long RecastReferenceWorkUnitsPerOperation = 160_000L;

        public static NavBakeEstimateReport Estimate(NavBakeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            context.Validate();

            if (context.Algorithm == NavBakeAlgorithmKind.LayeredSpan)
            {
                throw new InvalidOperationException(
                    "NavBakeEstimator does not support algorithm 'layered-span'; estimation metrics are not implemented.");
            }

            int cellCm;
            int verticalCellCm;
            int fullTileCountX;
            int fullTileCountY;
            int tileWidthCm;
            int tileHeightCm;
            int terrainWidthCells;
            int terrainHeightCells;
            int terrainChunkCells;
            long terrainCellSampleCount;
            string terrainContentHash;
            LogicTerrainField? terrainForHash = null;

            switch (context.InputKind)
            {
                case NavBakeInputKind.LogicTerrain:
                {
                    LogicTerrainField terrain = context.RequireTerrain();
                    terrainForHash = terrain;
                    cellCm = terrain.HorizontalStepCm;
                    verticalCellCm = terrain.VerticalStepCm;
                    if (cellCm <= 0 || verticalCellCm <= 0)
                    {
                        throw new InvalidOperationException("NavBakeContext.terrain cell size must be > 0.");
                    }

                    fullTileCountX = terrain.WidthChunks;
                    fullTileCountY = terrain.HeightChunks;
                    tileWidthCm = checked(terrain.ChunkSizeCells * cellCm);
                    tileHeightCm = checked(terrain.ChunkSizeCells * verticalCellCm);
                    terrainWidthCells = terrain.WidthCells;
                    terrainHeightCells = terrain.HeightCells;
                    terrainChunkCells = terrain.ChunkSizeCells;
                    terrainCellSampleCount = CountTargetTerrainCells(context, terrain);
                    terrainContentHash = ComputeTargetTerrainHash(context, terrain);
                    break;
                }
                case NavBakeInputKind.TriangleSurface:
                {
                    NavTriangleSurfaceTileIndex surfaceIndex = context.RequireTriangleSurface();
                    NavTriangleSurfaceTileGrid grid = surfaceIndex.Grid;
                    cellCm = SpatialScaleDefaults.CellCm;
                    verticalCellCm = SpatialScaleDefaults.CellCm;
                    fullTileCountX = grid.TileCountX;
                    fullTileCountY = grid.TileCountZ;
                    tileWidthCm = grid.TileWidthCm;
                    tileHeightCm = grid.TileHeightCm;
                    terrainWidthCells = checked(fullTileCountX * (tileWidthCm / cellCm));
                    terrainHeightCells = checked(fullTileCountY * (tileHeightCm / verticalCellCm));
                    terrainChunkCells = tileWidthCm / cellCm;
                    terrainCellSampleCount = CountTargetSurfaceCells(grid, context.Targets, cellCm, verticalCellCm);
                    terrainContentHash = ComputeTargetTriangleSurfaceHash(surfaceIndex, context.Targets);
                    break;
                }
                default:
                    throw new InvalidOperationException($"Unknown NavBakeInputKind '{context.InputKind}'.");
            }

            int fullTileCount = checked(fullTileCountX * fullTileCountY);
            int targetTileCount = context.Targets.Count;
            int layerCount = context.Config.Layers.Count;
            int profileCount = context.Config.Profiles.Count;
            int operationCount = checked(targetTileCount * layerCount * profileCount);
            int workers = context.Execution.Parallel
                ? Math.Max(1, context.Execution.MaxDegreeOfParallelism)
                : 1;

            var profiles = new List<NavBakeProfileEstimate>(profileCount);
            long recastColumnBudgetTotal = 0;

            for (int i = 0; i < context.Config.Profiles.Count; i++)
            {
                NavMeshAgentProfileConfig navProfile = context.Config.Profiles[i]
                    ?? throw new InvalidOperationException($"NavBakeContext.config.profiles[{i}] is null.");
                AgentProfileConfig agentProfile = context.AgentProfiles.Require(navProfile.Id, $"{NavMeshConfigPaths.BakeConfigPath}.profiles[{i}]");
                NavBakeProfileEstimate profile = EstimateProfile(agentProfile, navProfile, tileWidthCm, context.Config.Recast);
                profiles.Add(profile);
                recastColumnBudgetTotal = checked(recastColumnBudgetTotal + (long)targetTileCount * layerCount * profile.RecastColumnBudgetPerTile);
            }

            long budgetWorkUnitCount;
            switch (context.Algorithm)
            {
                case NavBakeAlgorithmKind.Recast:
                    budgetWorkUnitCount = recastColumnBudgetTotal;
                    break;
                case NavBakeAlgorithmKind.ExactCdt:
                    budgetWorkUnitCount = checked(terrainCellSampleCount * layerCount * profileCount);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(context.Algorithm), context.Algorithm, $"Unknown nav bake algorithm kind '{context.Algorithm}'.");
            }
            NavBakeBudgetStatus budgetStatus = GetBudgetStatus(budgetWorkUnitCount);
            long estimatedBytesLow = checked((long)operationCount * EstimatedBytesPerOperationLow);
            long estimatedBytesHigh = checked((long)operationCount * EstimatedBytesPerOperationHigh);
            GetMsBand(context.Algorithm, out float lowMs, out float highMs);
            double normalizedOperationCount = Math.Max(operationCount, budgetWorkUnitCount / (double)GetReferenceWorkUnitsPerOperation(context.Algorithm));
            double serialLow = normalizedOperationCount * lowMs / 1000d;
            double serialHigh = normalizedOperationCount * highMs / 1000d;
            double estimatedLow = serialLow / workers;
            double estimatedHigh = serialHigh / workers;
            string estimateHash = ComputeEstimateHash(
                context,
                terrainForHash,
                cellCm,
                verticalCellCm,
                operationCount,
                budgetWorkUnitCount,
                terrainContentHash);

            return new NavBakeEstimateReport
            {
                MapId = context.MapId,
                SourceUri = context.SourceUri,
                Mode = NavBakeNames.FormatMode(context.Mode),
                Algorithm = NavBakeNames.FormatAlgorithm(context.Algorithm),
                EstimateHash = estimateHash,
                TerrainContentHash = terrainContentHash,
                TerrainWidthCells = terrainWidthCells,
                TerrainHeightCells = terrainHeightCells,
                TerrainChunkCells = terrainChunkCells,
                CellCm = cellCm,
                TileWorldWidthCm = tileWidthCm,
                TileWorldHeightCm = tileHeightCm,
                FullTileCountX = fullTileCountX,
                FullTileCountY = fullTileCountY,
                FullTileCount = fullTileCount,
                TargetTileCount = targetTileCount,
                LayerCount = layerCount,
                ProfileCount = profileCount,
                BakeOperationCount = operationCount,
                ObstacleCount = context.Obstacles.ObstacleCount,
                TerrainCellSampleCount = terrainCellSampleCount,
                RecastColumnBudgetTotal = recastColumnBudgetTotal,
                BudgetWorkUnitCount = budgetWorkUnitCount,
                EstimatedTileBytesLow = estimatedBytesLow,
                EstimatedTileBytesHigh = estimatedBytesHigh,
                EffectiveWorkers = workers,
                EstimatedSerialSecondsLow = serialLow,
                EstimatedSerialSecondsHigh = serialHigh,
                EstimatedSecondsLow = estimatedLow,
                EstimatedSecondsHigh = estimatedHigh,
                BudgetStatus = budgetStatus,
                BudgetStatusText = FormatBudgetStatus(budgetStatus),
                BudgetMessage = FormatBudgetMessage(budgetStatus, operationCount, budgetWorkUnitCount),
                RequiresExplicitLargeBakeApproval = budgetStatus == NavBakeBudgetStatus.Large,
                Profiles = profiles
            };
        }

        public static void EnsureBakeAllowed(
            NavBakeEstimateReport estimate,
            bool largeBakeApproved,
            string? acceptedEstimateHash)
        {
            if (estimate == null) throw new ArgumentNullException(nameof(estimate));

            if (estimate.BudgetStatus == NavBakeBudgetStatus.Reject)
            {
                throw new InvalidOperationException(estimate.BudgetMessage);
            }

            if (estimate.BudgetStatus == NavBakeBudgetStatus.Large)
            {
                if (!largeBakeApproved)
                {
                    throw new InvalidOperationException(
                        $"{estimate.BudgetMessage} Pass an explicit large-bake approval and matching estimate hash before writing outputs.");
                }

                if (string.IsNullOrWhiteSpace(acceptedEstimateHash) ||
                    !string.Equals(acceptedEstimateHash, estimate.EstimateHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Large nav bake requires a matching estimateHash from the current estimate.");
                }
            }
        }

        private static NavBakeProfileEstimate EstimateProfile(
            AgentProfileConfig agentProfile,
            NavMeshAgentProfileConfig navProfile,
            int tileWidthCm,
            NavRecastConfig recast)
        {
            if (navProfile.MaxClimbCm < 0)
            {
                throw new InvalidOperationException($"NavMeshBakeConfig.profile '{navProfile.Id}' requires maxClimbCm >= 0.");
            }

            if (navProfile.MaxSlopeDeg < 0f || navProfile.MaxSlopeDeg >= 90f || float.IsNaN(navProfile.MaxSlopeDeg))
            {
                throw new InvalidOperationException($"NavMeshBakeConfig.profile '{navProfile.Id}' requires maxSlopeDeg >= 0 and < 90.");
            }

            float recastCellSizeCm = recast.RasterCellSizeCm;
            float recastCellHeightCm = recast.RasterCellHeightCm;
            int columnsPerAxis = (int)MathF.Ceiling(tileWidthCm / recastCellSizeCm);
            int columnBudget = checked(columnsPerAxis * columnsPerAxis);
            int walkableHeightVoxels = (int)MathF.Ceiling(agentProfile.HeightCm / recastCellHeightCm);
            int walkableClimbVoxels = (int)MathF.Floor(navProfile.MaxClimbCm / recastCellHeightCm);

            return new NavBakeProfileEstimate
            {
                ProfileId = navProfile.Id,
                AgentRadiusCm = agentProfile.RadiusCm,
                AgentHeightCm = agentProfile.HeightCm,
                AgentClearanceCm = agentProfile.ClearanceCm,
                MaxClimbCm = navProfile.MaxClimbCm,
                MaxSlopeDeg = navProfile.MaxSlopeDeg,
                MinWalkableUpDot = MathF.Cos(navProfile.MaxSlopeDeg * MathF.PI / 180f),
                RecastCellSizeCm = recastCellSizeCm,
                RecastCellHeightCm = recastCellHeightCm,
                RecastColumnsPerAxis = columnsPerAxis,
                RecastColumnBudgetPerTile = columnBudget,
                WalkableHeightVoxels = walkableHeightVoxels,
                WalkableClimbVoxels = walkableClimbVoxels
            };
        }

        private static void GetMsBand(NavBakeAlgorithmKind algorithm, out float lowMs, out float highMs)
        {
            switch (algorithm)
            {
                case NavBakeAlgorithmKind.ExactCdt:
                    lowMs = SimpleMsPerOperationLow;
                    highMs = SimpleMsPerOperationHigh;
                    return;
                case NavBakeAlgorithmKind.Recast:
                    lowMs = RecastMsPerOperationLow;
                    highMs = RecastMsPerOperationHigh;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, $"Unknown nav bake algorithm kind '{algorithm}'.");
            }
        }

        private static long GetReferenceWorkUnitsPerOperation(NavBakeAlgorithmKind algorithm)
        {
            return algorithm switch
            {
                NavBakeAlgorithmKind.ExactCdt => ExactCdtReferenceWorkUnitsPerOperation,
                NavBakeAlgorithmKind.Recast => RecastReferenceWorkUnitsPerOperation,
                _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, $"Unknown nav bake algorithm kind '{algorithm}'.")
            };
        }

        private static NavBakeBudgetStatus GetBudgetStatus(long budgetWorkUnitCount)
        {
            if (budgetWorkUnitCount > LargeWorkUnitThreshold) return NavBakeBudgetStatus.Reject;
            if (budgetWorkUnitCount > OkWorkUnitThreshold) return NavBakeBudgetStatus.Large;
            return NavBakeBudgetStatus.Ok;
        }

        private static string FormatBudgetStatus(NavBakeBudgetStatus status)
        {
            return status switch
            {
                NavBakeBudgetStatus.Ok => "ok",
                NavBakeBudgetStatus.Large => "large",
                NavBakeBudgetStatus.Reject => "reject",
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
            };
        }

        private static string FormatBudgetMessage(NavBakeBudgetStatus status, int operationCount, long budgetWorkUnitCount)
        {
            return status switch
            {
                NavBakeBudgetStatus.Ok => $"ok: {operationCount} operations / {budgetWorkUnitCount} work units can run directly.",
                NavBakeBudgetStatus.Large => $"large: {operationCount} operations / {budgetWorkUnitCount} work units require an explicit large-bake action.",
                NavBakeBudgetStatus.Reject => $"reject: {operationCount} operations / {budgetWorkUnitCount} work units require a profiled bake-farm flow.",
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
            };
        }

        private static long CountTargetTerrainCells(NavBakeContext context, LogicTerrainField terrain)
        {
            long count = 0;
            for (int i = 0; i < context.Targets.Count; i++)
            {
                NavBakeTileCoord target = context.Targets[i];
                count = checked(count + (long)terrain.TileWidthCells(target.ChunkX) * terrain.TileHeightCells(target.ChunkY));
            }

            return count;
        }

        private static string ComputeTargetTerrainHash(NavBakeContext context, LogicTerrainField terrain)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> buffer = stackalloc byte[SpatialScaleDefaults.BitsPerFlagWord];
            for (int i = 0; i < context.Targets.Count; i++)
            {
                NavBakeTileCoord target = context.Targets[i];
                AppendInt32(hash, buffer, target.ChunkX);
                AppendInt32(hash, buffer, target.ChunkY);

                int startCol = target.ChunkX * terrain.ChunkSizeCells;
                int startRow = target.ChunkY * terrain.ChunkSizeCells;
                int width = terrain.TileWidthCells(target.ChunkX);
                int height = terrain.TileHeightCells(target.ChunkY);
                for (int row = 0; row < height; row++)
                {
                    for (int col = 0; col < width; col++)
                    {
                        int globalCol = startCol + col;
                        int globalRow = startRow + row;
                        LogicTerrainCell cell = terrain.GetCell(globalCol, globalRow);
                        buffer[0] = cell.HeightLevel;
                        buffer[1] = cell.WaterHeightLevel;
                        buffer[2] = (byte)cell.SurfaceFlags;
                        buffer[3] = cell.AreaId;
                        BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(4, 4), BitConverter.SingleToInt32Bits(cell.Cost));
                        for (int edge = 0; edge < 3; edge++)
                        {
                            buffer[8 + edge] = terrain.TryGetCliffStraightenEdge(globalCol, globalRow, edge, out bool value) && value
                                ? (byte)1
                                : (byte)0;
                        }

                        hash.AppendData(buffer.Slice(0, 11));
                    }
                }
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        private static void AppendInt32(IncrementalHash hash, Span<byte> buffer, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(0, 4), value);
            hash.AppendData(buffer.Slice(0, 4));
        }

        private static long CountTargetSurfaceCells(
            NavTriangleSurfaceTileGrid grid,
            IReadOnlyList<NavBakeTileCoord> targets,
            int cellCm,
            int verticalCellCm)
        {
            int cellsPerTileX = grid.TileWidthCm / cellCm;
            int cellsPerTileY = grid.TileHeightCm / verticalCellCm;
            if (cellsPerTileX <= 0 || cellsPerTileY <= 0)
            {
                throw new InvalidOperationException("NavBakeContext.triangleSurface.grid tile size must be >= one cell.");
            }

            long cellsPerTile = checked((long)cellsPerTileX * cellsPerTileY);
            return checked(cellsPerTile * targets.Count);
        }

        private static string ComputeTargetTriangleSurfaceHash(
            NavTriangleSurfaceTileIndex surfaceIndex,
            IReadOnlyList<NavBakeTileCoord> targets)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> buffer = stackalloc byte[16];
            NavTriangleSurfaceSnapshot surface = surfaceIndex.Surface;
            ReadOnlySpan<int> stableIds = surface.TriStableIds;
            AppendInt32(hash, buffer, surfaceIndex.Grid.HaloPaddingCm);
            AppendInt32(hash, buffer, surface.TriangleCount);
            for (int i = 0; i < targets.Count; i++)
            {
                NavBakeTileCoord target = targets[i];
                AppendInt32(hash, buffer, target.ChunkX);
                AppendInt32(hash, buffer, target.ChunkY);
                ReadOnlySpan<int> indices = surfaceIndex.GetTriangleIndices(target);
                AppendInt32(hash, buffer, indices.Length);
                for (int t = 0; t < indices.Length; t++)
                {
                    AppendInt32(hash, buffer, stableIds[indices[t]]);
                }
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        private static string ComputeEstimateHash(
            NavBakeContext context,
            LogicTerrainField? terrain,
            int cellCm,
            int verticalCellCm,
            int operationCount,
            long budgetWorkUnitCount,
            string terrainContentHash)
        {
            using var sha = SHA256.Create();
            var sb = new StringBuilder(1024);
            sb.Append("v3|")
                .Append(context.MapId).Append('|')
                .Append(context.ModId).Append('|')
                .Append(context.SourceUri).Append('|')
                .Append(terrainContentHash).Append('|')
                .Append(NavBakeNames.FormatMode(context.Mode)).Append('|')
                .Append(NavBakeNames.FormatAlgorithm(context.Algorithm)).Append('|')
                .Append(NavBakeAdapterCapability.FormatInputKind(context.InputKind)).Append('|');

            if (terrain != null)
            {
                sb.Append(terrain.Topology).Append('|')
                    .Append(terrain.WidthCells).Append('x').Append(terrain.HeightCells).Append('|')
                    .Append(terrain.ChunkSizeCells).Append('|');
            }
            else
            {
                NavTriangleSurfaceTileGrid grid = context.RequireTriangleSurface().Grid;
                sb.Append("triangle-surface|")
                    .Append(grid.TileCountX).Append('x').Append(grid.TileCountZ).Append('|')
                    .Append(grid.TileWidthCm).Append('x').Append(grid.TileHeightCm).Append('|')
                    .Append(grid.HaloPaddingCm).Append('|');
            }

            sb.Append(cellCm).Append('x').Append(verticalCellCm).Append('|')
                .Append(context.TileVersion).Append('|')
                .Append(context.BuildConfig.HeightScaleMeters.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(context.BuildConfig.MinWalkableUpDot.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(context.BuildConfig.CliffHeightThreshold).Append('|')
                .Append(context.Execution.Parallel).Append('|')
                .Append(context.Execution.MaxDegreeOfParallelism).Append('|')
                .Append(operationCount).Append('|')
                .Append(budgetWorkUnitCount).Append('|');

            AppendTargets(sb, context.Targets);
            AppendLayers(sb, context.Config.Layers);
            AppendProfiles(sb, context.Config.Profiles, context.AgentProfiles);
            if (context.Algorithm == NavBakeAlgorithmKind.Recast)
            {
                NavRecastConfig recast = context.Config.Recast;
                sb.Append("recast:").Append(recast.RasterCellSizeCm).Append('x').Append(recast.RasterCellHeightCm).Append('|');
            }
            AppendObstacles(sb, context.Obstacles);

            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static void AppendTargets(StringBuilder sb, IReadOnlyList<NavBakeTileCoord> targets)
        {
            sb.Append("targets=");
            for (int i = 0; i < targets.Count; i++)
            {
                NavBakeTileCoord target = targets[i];
                sb.Append(target.ChunkX).Append(',').Append(target.ChunkY).Append(';');
            }
            sb.Append('|');
        }

        private static void AppendLayers(StringBuilder sb, IReadOnlyList<NavLayerConfig> layers)
        {
            sb.Append("layers=");
            for (int i = 0; i < layers.Count; i++)
            {
                NavLayerConfig layer = layers[i];
                sb.Append(layer.Id).Append(':').Append(layer.Layer).Append(';');
            }
            sb.Append('|');
        }

        private static void AppendProfiles(
            StringBuilder sb,
            IReadOnlyList<NavMeshAgentProfileConfig> profiles,
            AgentProfileRegistry agentProfiles)
        {
            sb.Append("profiles=");
            for (int i = 0; i < profiles.Count; i++)
            {
                NavMeshAgentProfileConfig navProfile = profiles[i];
                AgentProfileConfig agent = agentProfiles.Require(navProfile.Id, $"{NavMeshConfigPaths.BakeConfigPath}.profiles[{i}]");
                sb.Append(navProfile.Id).Append(':')
                    .Append(navProfile.MaxClimbCm).Append(':')
                    .Append(navProfile.MaxSlopeDeg.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(agent.RadiusCm.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(agent.HeightCm.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(agent.ClearanceCm.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            }
            sb.Append('|');
        }

        private static void AppendObstacles(StringBuilder sb, INavObstacleSource obstacles)
        {
            sb.Append("obstacles=");
            for (int i = 0; i < obstacles.ObstacleCount; i++)
            {
                obstacles.AppendHash(i, sb);
            }
        }
    }
}
