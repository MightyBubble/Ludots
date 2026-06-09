using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Diagnostics;
using Ludots.Core.Navigation.NavMesh.LogicHeightmap;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Navigation.Pathing.Config;

namespace MassNavigationMod.Runtime;

public readonly record struct MassNavigationPathOnlyQueryDiagnostics(
    bool Available,
    string Status,
    bool NoOrderSubmitted,
    string PreviewMode,
    string InputContract,
    string RoutePreviewState,
    bool HighlightRouteVisible,
    string OrderSuppressionReason,
    string PathPointContract,
    string WaypointContract,
    string RouteProvenance,
    string QuerySource,
    string Strategy,
    string AgentTypeId,
    string NavProfileId,
    int Layer,
    Vector2 StartWorldCm,
    Vector2 GoalWorldCm,
    int WaypointCount,
    int PathPointCount,
    int ExpandedNodeCount,
    int ErrorCode,
    int TouchedTileCount,
    int CorridorPortalCount,
    float TravelCost,
    int StartMacroChunkX,
    int StartMacroChunkY,
    int GoalMacroChunkX,
    int GoalMacroChunkY,
    int MacroRouteChunkCount,
    int MacroExpandedChunkCount);

public readonly record struct MassNavigationPathPointSample(
    int Xcm,
    int Ycm);

public readonly record struct MassNavigationTargetSlotSample(
    int Xcm,
    int Ycm);

public readonly record struct MassNavigationOrderReuseDiagnostics(
    bool HasOrder,
    int LastOrderId,
    string NormalizedKey,
    bool CacheHit,
    int ReusedRouteId,
    int RouteCacheSize,
    int FanoutCount,
    int SamePointReuseCount,
    int NearPointReuseCount,
    string Strategy,
    string AgentTypeId,
    string NavProfileId,
    int Layer,
    string DataVersion,
    int DynamicBlockerEpoch,
    string InvalidationReason,
    string CacheSource,
    string ReuseScope,
    string PathRouteSignature,
    string PathRouteSource,
    int PathRoutePointCount,
    int PathRouteTouchedTileCount,
    string MeshRouteSignature,
    string MeshRouteSource,
    string MeshRouteStatus,
    int MeshRouteTouchedTileCount,
    string ProductionGap);

public readonly record struct MassNavigationTargetAllocationDiagnostics(
    bool HasAllocation,
    int SelectedCount,
    int SlotCount,
    int ReachableSlotCount,
    int ReachabilityFanoutCount,
    int BlockedSlotCount,
    int FallbackSlotCount,
    int GroupSlotCount,
    int UnitSlotCount,
    float GoalFootprintRadiusCm,
    string FormationMode,
    Vector2 DestinationWorldCm,
    string ReachabilityProbeStatus,
    string ReachabilitySource,
    string AllocationRouteReuseKey,
    int AllocationRouteId,
    string AllocationRouteCacheSource,
    string MeshReachabilitySource,
    string MeshReachabilityStatus,
    int MeshReachabilityTouchedTileCount,
    string BlockedReasonSummary,
    string FallbackReasonSummary,
    string ProductionGap,
    int ActualTargetSampleCount,
    string ActualTargetSampleSource);

public readonly record struct MassNavigationLayerCostDiagnostics(
    string AgentTypeId,
    string NavProfileId,
    int Layer,
    string SelectionMode,
    int NavAreaCostCount,
    int GraphTagRuleCount,
    int ForbiddenAreaCount,
    float RepresentativeAreaCost,
    string AreaCostSamples,
    string GraphRuleSummary,
    string RequiredTagSummary,
    string ForbiddenTagSummary);

public readonly record struct MassNavigationStrategySwitchDiagnostics(
    string AgentTypeId,
    string RequestedMode,
    string SelectedStrategy,
    bool GraphQueryAvailable,
    string GraphStatus,
    int GraphPathPointCount,
    int GraphExpandedNodeCount,
    float GraphTravelCost,
    bool MeshQueryAvailable,
    string MeshStatus,
    int MeshPathPointCount,
    int MeshExpandedNodeCount,
    float MeshTravelCost,
    string MeshQuerySource,
    int MeshStartChunkX,
    int MeshStartChunkY,
    int MeshGoalChunkX,
    int MeshGoalChunkY,
    int MeshTouchedTileCount,
    string CostBreakdown,
    int RouteId,
    string AcceptanceProof);

public readonly record struct MassNavigationWaypointPathDiagnostics(
    int WaypointCount,
    int PathPointCount,
    bool WaypointsEditable,
    bool PathPointsImmutable,
    bool PathPointsCanSeedWaypoints,
    string Source,
    string BusinessExample,
    bool HasAuthoredPlan,
    int EditRevision,
    int InvalidatedPathPointCount,
    Vector2 AuthoredMidpointWorldCm,
    string EditState);

public readonly record struct MassNavigationObstacleDiagnostics(
    int TargetStaticObstacleCount,
    int AuthoredStaticObstacleCount,
    int BakedStaticObstacleCount,
    int LoadedStaticObstacleCount,
    int SolverActiveStaticObstacleCount,
    int SolverStaticObstacleCapacity,
    string Source);

public readonly record struct MassNavigationStaticObstacleWorldDiagnostics(
    int TargetStaticObstacleCount,
    int PlannedWorldObstacleCount,
    int MacroChunkColumns,
    int MacroChunkRows,
    int MacroChunkCoverageCount,
    int ActiveWindowLoadedCount,
    int SolverActiveStaticObstacleCount,
    int SolverStaticObstacleCapacity,
    int DeterministicSeed,
    string DistributionStrategy,
    string SampleChunkBuckets,
    bool WorldDistributionReady,
    bool ActiveWindowLimited,
    string DataSource,
    string RuntimeActivationStrategy);

public readonly record struct MassNavigationHpaMacroDiagnostics(
    bool Available,
    int MacroChunkColumns,
    int MacroChunkRows,
    int MacroChunkCount,
    int MacroChunkSizeXCm,
    int MacroChunkSizeYCm,
    int ExpectedAdjacencyEdgeCount,
    int SamplePortalCount,
    int SampleRouteChunkCount,
    int SampleExpandedChunkCount,
    int StartMacroChunkX,
    int StartMacroChunkY,
    int GoalMacroChunkX,
    int GoalMacroChunkY,
    string RouteSource,
    bool UsesSyntheticMacroGridTarget,
    string ProductionGap);

internal readonly record struct QueryProbe(
    bool Available,
    string Status,
    int PointCount,
    int Expanded,
    float TravelCost,
    string QuerySource,
    int StartChunkX,
    int StartChunkY,
    int GoalChunkX,
    int GoalChunkY,
    int TouchedTileCount,
    string AcceptanceProof,
    int StartWorldX,
    int StartWorldY,
    int GoalWorldX,
    int GoalWorldY)
{
    public static QueryProbe Unavailable(string status, string acceptanceProof)
    {
        return new QueryProbe(
            Available: false,
            Status: status,
            PointCount: 0,
            Expanded: 0,
            TravelCost: 0f,
            QuerySource: "not_available",
            StartChunkX: -1,
            StartChunkY: -1,
            GoalChunkX: -1,
            GoalChunkY: -1,
            TouchedTileCount: 0,
            AcceptanceProof: acceptanceProof,
            StartWorldX: 0,
            StartWorldY: 0,
            GoalWorldX: 0,
            GoalWorldY: 0);
    }
}

public sealed class MassNavigationActiveWindowNavMeshProbe
{
    private readonly IVirtualFileSystem _vfs;
    private readonly IReadOnlyList<string> _loadedModIds;
    private readonly string _mapId;
    private readonly NavBakeDiagnosticsDocument? _diagnostics;
    private readonly MassNavigationBakeDataDiagnostics? _bakeDataDiagnostics;

    public MassNavigationActiveWindowNavMeshProbe(
        IVirtualFileSystem vfs,
        IEnumerable<string>? loadedModIds,
        string mapId,
        NavBakeDiagnosticsDocument? diagnostics,
        MassNavigationBakeDataDiagnostics? bakeDataDiagnostics = null)
    {
        _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
        _loadedModIds = loadedModIds != null
            ? new List<string>(loadedModIds)
            : Array.Empty<string>();
        _mapId = string.IsNullOrWhiteSpace(mapId)
            ? throw new ArgumentException("mapId is required.", nameof(mapId))
            : mapId;
        _diagnostics = diagnostics;
        _bakeDataDiagnostics = bakeDataDiagnostics;
    }

    internal QueryProbe Run(MassNavigationBakeDataProfileSummary profile)
    {
        if (_diagnostics?.LayerProfiles == null || _diagnostics.LayerProfiles.Count == 0)
        {
            return QueryProbe.Unavailable("NavBakeDiagnosticsMissing", "active_window_navmesh_diagnostics_missing");
        }

        NavBakeLayerProfileSummary? bakedProfile = ResolveBakedProfile(profile);
        if (bakedProfile == null)
        {
            return QueryProbe.Unavailable(
                "ProfileTileMissing",
                $"active_window_navmesh_tiles_missing_for_layer{profile.Layer}_profile_{profile.NavProfileId}");
        }

        int minX = _diagnostics.ActiveWindowMinChunkX >= 0 ? _diagnostics.ActiveWindowMinChunkX : 0;
        int minY = _diagnostics.ActiveWindowMinChunkY >= 0 ? _diagnostics.ActiveWindowMinChunkY : 0;
        int maxX = _diagnostics.ActiveWindowMaxChunkX >= minX ? _diagnostics.ActiveWindowMaxChunkX : minX;
        int maxY = _diagnostics.ActiveWindowMaxChunkY >= minY ? _diagnostics.ActiveWindowMaxChunkY : minY;
        if (maxX < minX || maxY < minY)
        {
            return QueryProbe.Unavailable("ActiveWindowMissing", "active_window_navmesh_bounds_missing");
        }

        var loadedTiles = new Dictionary<NavTileId, NavTile>();
        PrimeProbeTiles(profile.Layer, profile.NavProfileId, minX, minY, maxX, maxY, loadedTiles);

        if (loadedTiles.Count == 0)
        {
            return QueryProbe.Unavailable(
                "TileLoadMissing",
                $"active_window_navmesh_tiles_unreadable_for_layer{profile.Layer}_profile_{profile.NavProfileId}");
        }

        if (!TryResolveRouteTiles(loadedTiles, minX, minY, maxX, maxY, out NavTile startTile, out NavTile goalTile))
        {
            return QueryProbe.Unavailable(
                "RouteTileMissing",
                $"active_window_navmesh_route_tiles_missing_for_layer{profile.Layer}_profile_{profile.NavProfileId}");
        }

        if (!TryCreateCoordinateMapper(startTile, out MassNavigationNavMeshRuntimeCoordinateMapper mapper))
        {
            return QueryProbe.Unavailable(
                "CoordinateMapperMissing",
                $"active_window_navmesh_runtime_coordinate_mapper_missing_for_layer{profile.Layer}_profile_{profile.NavProfileId}");
        }

        Vector2 start = ResolveTileSampleWorldCm(startTile, mapper, preferNearMin: true);
        Vector2 goal = ResolveTileSampleWorldCm(goalTile, mapper, preferNearMin: false);
        int startX = (int)MathF.Round(start.X);
        int startY = (int)MathF.Round(start.Y);
        int goalX = (int)MathF.Round(goal.X);
        int goalY = (int)MathF.Round(goal.Y);
        var store = new NavTileStore(
            id =>
            {
                if (!TryResolveTileUri(profile.Layer, profile.NavProfileId, id.ChunkX, id.ChunkY, out string? uri))
                {
                    throw new FileNotFoundException($"NavTile not found for {id}.");
                }

                return _vfs.GetStream(uri!);
            },
            mapper.RuntimeTileWidthCm,
            mapper.RuntimeTileHeightCm,
            mapper.WorldMinXcm,
            mapper.WorldMinYcm,
            mapper.BakedTileWidthCm,
            mapper.BakedTileHeightCm);
        var query = new NavQueryService(store, profile.Layer, BuildAreaCosts(profile));
        NavPathResult result = query.TryFindPath(startX, startY, goalX, goalY, maxPortals: 128);
        if (result.Status != NavPathStatus.Ok || result.PathXcm.Length < 2)
        {
            return new QueryProbe(
                Available: false,
                Status: result.Status.ToString(),
                PointCount: 0,
                Expanded: loadedTiles.Count,
                TravelCost: 0f,
                QuerySource: "active_window_navmesh_query",
                StartChunkX: startTile.TileId.ChunkX,
                StartChunkY: startTile.TileId.ChunkY,
                GoalChunkX: goalTile.TileId.ChunkX,
                GoalChunkY: goalTile.TileId.ChunkY,
                TouchedTileCount: loadedTiles.Count,
                AcceptanceProof: "active_window_navmesh_query_not_reachable",
                StartWorldX: startX,
                StartWorldY: startY,
                GoalWorldX: goalX,
                GoalWorldY: goalY);
        }

        return new QueryProbe(
            Available: true,
            Status: "Ok",
            PointCount: result.PathXcm.Length,
            Expanded: loadedTiles.Count,
            TravelCost: result.TravelCost.ToFloat(),
            QuerySource: "active_window_navmesh_query",
            StartChunkX: startTile.TileId.ChunkX,
            StartChunkY: startTile.TileId.ChunkY,
            GoalChunkX: goalTile.TileId.ChunkX,
            GoalChunkY: goalTile.TileId.ChunkY,
            TouchedTileCount: EstimateTouchedTileCount(result.PathXcm, result.PathZcm, result.PathXcm.Length, startTile, goalTile),
            AcceptanceProof: "active_window_navmesh_query_passed_with_tile_route_layer_profile_costs_and_touched_tile_provenance",
            StartWorldX: startX,
            StartWorldY: startY,
            GoalWorldX: goalX,
            GoalWorldY: goalY);
    }

    internal QueryProbe Run(MassNavigationLayerCostDiagnostics profile)
    {
        return Run(new MassNavigationBakeDataProfileSummary(
            profile.AgentTypeId,
            profile.NavProfileId,
            profile.Layer,
            profile.SelectionMode,
            profile.NavAreaCostCount,
            profile.GraphTagRuleCount,
            profile.ForbiddenAreaCount,
            profile.RepresentativeAreaCost,
            profile.AreaCostSamples,
            profile.GraphRuleSummary,
            profile.RequiredTagSummary,
            profile.ForbiddenTagSummary));
    }

    private NavBakeLayerProfileSummary? ResolveBakedProfile(MassNavigationBakeDataProfileSummary profile)
    {
        for (int i = 0; i < _diagnostics!.LayerProfiles.Count; i++)
        {
            NavBakeLayerProfileSummary item = _diagnostics.LayerProfiles[i];
            if (item.Layer == profile.Layer &&
                item.BakedTiles > 0 &&
                string.Equals(item.ProfileId, profile.NavProfileId, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private bool TryLoadTile(int layer, string profileId, int chunkX, int chunkY, out NavTile? tile)
    {
        tile = null;
        if (!TryResolveTileUri(layer, profileId, chunkX, chunkY, out string? uri))
        {
            return false;
        }

        using Stream stream = _vfs.GetStream(uri!);
        tile = NavTileBinary.Read(stream);
        return true;
    }

    private void PrimeProbeTiles(
        int layer,
        string profileId,
        int minX,
        int minY,
        int maxX,
        int maxY,
        Dictionary<NavTileId, NavTile> loadedTiles)
    {
        int centerX = minX + ((maxX - minX) / 2);
        int centerY = minY + ((maxY - minY) / 2);
        Span<(int X, int Y)> samples = stackalloc (int X, int Y)[9]
        {
            (minX, minY),
            (Math.Min(maxX, minX + 1), minY),
            (minX, Math.Min(maxY, minY + 1)),
            (centerX, centerY),
            (Math.Min(maxX, centerX + 1), centerY),
            (centerX, Math.Min(maxY, centerY + 1)),
            (maxX, maxY),
            (Math.Max(minX, maxX - 1), maxY),
            (maxX, Math.Max(minY, maxY - 1))
        };

        for (int i = 0; i < samples.Length; i++)
        {
            (int x, int y) = samples[i];
            NavTileId id = new(x, y, layer);
            if (loadedTiles.ContainsKey(id))
            {
                continue;
            }

            if (TryLoadTile(layer, profileId, x, y, out NavTile? tile) && tile != null)
            {
                loadedTiles[tile.TileId] = tile;
            }
        }
    }

    private bool TryResolveTileUri(int layer, string profileId, int chunkX, int chunkY, out string? uri)
    {
        string relativePath = NavAssetPaths.GetNavTileRelativePath(_mapId, layer, profileId, chunkX, chunkY);
        foreach (string candidate in EnumerateCandidateUris(relativePath))
        {
            if (_vfs.TryResolveFullPath(candidate, out string fullPath) && File.Exists(fullPath))
            {
                uri = candidate;
                return true;
            }
        }

        uri = null;
        return false;
    }

    private IEnumerable<string> EnumerateCandidateUris(string relativePath)
    {
        yield return $"Core:{relativePath}";
        if (TryStripAssetsPrefix(relativePath, out string coreRelativePath))
        {
            yield return $"Core:{coreRelativePath}";
        }

        for (int i = 0; i < _loadedModIds.Count; i++)
        {
            string modId = _loadedModIds[i];
            if (!string.IsNullOrWhiteSpace(modId))
            {
                yield return $"{modId}:{relativePath}";
            }
        }
    }

    private static bool TryResolveRouteTiles(
        Dictionary<NavTileId, NavTile> loadedTiles,
        int minX,
        int minY,
        int maxX,
        int maxY,
        out NavTile startTile,
        out NavTile goalTile)
    {
        startTile = null!;
        goalTile = null!;
        int startBest = int.MaxValue;
        int goalBest = int.MaxValue;
        foreach (NavTile tile in loadedTiles.Values)
        {
            if (tile.TriangleCount <= 0)
            {
                continue;
            }

            int startDistance = Math.Abs(tile.TileId.ChunkX - minX) + Math.Abs(tile.TileId.ChunkY - minY);
            if (startDistance < startBest)
            {
                startBest = startDistance;
                startTile = tile;
            }

            int goalDistance = Math.Abs(tile.TileId.ChunkX - maxX) + Math.Abs(tile.TileId.ChunkY - maxY);
            if (goalDistance < goalBest)
            {
                goalBest = goalDistance;
                goalTile = tile;
            }
        }

        return startTile != null && goalTile != null;
    }

    private bool TryCreateCoordinateMapper(
        NavTile sampleTile,
        out MassNavigationNavMeshRuntimeCoordinateMapper mapper)
    {
        mapper = default;
        MassNavigationBakeDataDiagnostics? bakeDataDiagnostics = _bakeDataDiagnostics;
        if (bakeDataDiagnostics == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(bakeDataDiagnostics.LogicHeightmapSource) &&
            File.Exists(bakeDataDiagnostics.LogicHeightmapSource))
        {
            try
            {
                using LogicHeightmapFileReader reader = LogicHeightmapFileReader.Open(bakeDataDiagnostics.LogicHeightmapSource);
                if (MassNavigationNavMeshRuntimeCoordinateMapper.TryCreate(
                        bakeDataDiagnostics,
                        reader,
                        out mapper))
                {
                    return true;
                }
            }
            catch (IOException)
            {
            }
            catch (InvalidDataException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        mapper = MassNavigationNavMeshRuntimeCoordinateMapper.CreateFromNavTile(bakeDataDiagnostics, sampleTile);
        return mapper.Available;
    }

    private static Vector2 ResolveTileSampleWorldCm(
        NavTile tile,
        MassNavigationNavMeshRuntimeCoordinateMapper mapper,
        bool preferNearMin)
    {
        int minX = int.MaxValue;
        int maxX = int.MinValue;
        int minZ = int.MaxValue;
        int maxZ = int.MinValue;
        for (int i = 0; i < tile.VertexCount; i++)
        {
            minX = Math.Min(minX, tile.VertexXcm[i]);
            maxX = Math.Max(maxX, tile.VertexXcm[i]);
            minZ = Math.Min(minZ, tile.VertexZcm[i]);
            maxZ = Math.Max(maxZ, tile.VertexZcm[i]);
        }

        if (minX == int.MaxValue ||
            maxX == int.MinValue ||
            minZ == int.MaxValue ||
            maxZ == int.MinValue)
        {
            return mapper.BakedTileLocalToWorldCm(tile, 0, 0);
        }

        int localX = preferNearMin
            ? minX + Math.Max(1, (maxX - minX) / 4)
            : minX + Math.Max(1, ((maxX - minX) * 3) / 4);
        int localZ = preferNearMin
            ? minZ + Math.Max(1, (maxZ - minZ) / 4)
            : minZ + Math.Max(1, ((maxZ - minZ) * 3) / 4);
        return mapper.BakedTileLocalToWorldCm(tile, localX, localZ);
    }

    private static NavAreaCostTable BuildAreaCosts(MassNavigationBakeDataProfileSummary profile)
    {
        Fix64[] costs = new Fix64[256];
        for (int i = 0; i < costs.Length; i++)
        {
            costs[i] = Fix64.OneValue;
        }

        foreach (string sample in SplitCsv(profile.AreaCostSamples))
        {
            int separator = sample.IndexOf(':');
            if (separator <= 0 || separator >= sample.Length - 1)
            {
                continue;
            }

            if (int.TryParse(sample.AsSpan(0, separator), out int areaId) &&
                areaId >= 0 &&
                areaId <= 255 &&
                float.TryParse(sample.AsSpan(separator + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float cost) &&
                cost > 0f &&
                !float.IsNaN(cost))
            {
                costs[areaId] = Fix64.FromFloat(cost);
            }
        }

        return new NavAreaCostTable(costs);
    }

    private static IEnumerable<string> SplitCsv(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            yield return parts[i];
        }
    }

    private static int EstimateTouchedTileCount(int[] xs, int[] zs, int count, NavTile startTile, NavTile goalTile)
    {
        if (count <= 0)
        {
            return 0;
        }

        int estimated = Math.Abs(goalTile.TileId.ChunkX - startTile.TileId.ChunkX) +
            Math.Abs(goalTile.TileId.ChunkY - startTile.TileId.ChunkY) +
            1;
        return Math.Max(estimated, Math.Min(count, Math.Max(1, count - 1)));
    }

    private static bool TryStripAssetsPrefix(string relativePath, out string stripped)
    {
        stripped = string.Empty;
        const string prefix = "assets/";
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        stripped = normalized[prefix.Length..];
        return stripped.Length > 0;
    }
}

public sealed class MassNavigationAcceptanceDiagnostics
{
    private const string PathPreviewMode = "path_preview";
    private const string PathPreviewInputContract = "pick_start_world_point_then_goal_world_point";
    private const string PathPreviewOrderSuppressionReason = "preview_query_does_not_enqueue_massNavigationMove";
    private const string PathPointContract = "immutable_query_result";
    private const string WaypointContract = "editable_order_intent";
    private const float NearOrderBucketCm = 1_000f;
    private readonly Dictionary<string, RouteCacheEntry> _routeIdsByKey = new(StringComparer.Ordinal);
    private readonly HashSet<int> _observedOrderIds = new();
    private int _nextRouteId = 1;
    private Vector2 _lastExactDestination;
    private bool _hasLastExactDestination;
    private MassNavigationLayerCostDiagnostics[] _layerCosts = Array.Empty<MassNavigationLayerCostDiagnostics>();
    private MassNavigationStrategySwitchDiagnostics[] _strategySwitches = Array.Empty<MassNavigationStrategySwitchDiagnostics>();
    private MassNavigationPathPointSample[] _pathOnlyPathPoints = Array.Empty<MassNavigationPathPointSample>();
    private MassNavigationPathPointSample[] _invalidatedWaypointPathPoints = Array.Empty<MassNavigationPathPointSample>();
    private MassNavigationTargetSlotSample[] _targetSlotSamples = Array.Empty<MassNavigationTargetSlotSample>();
    private MassNavigationWaypointPathDiagnostics _waypointPath = CreateDefaultWaypointPathDiagnostics("not_bound");
    private int _waypointPlanEditRevision;
    private MassNavigationLayerCostDiagnostics _defaultProfile;
    private bool _hasDefaultProfile;
    private MassNavigationBakeDataDiagnostics? _diagnostics;
    private bool _hasReusablePathQueryEndpoints;

    public MassNavigationPathOnlyQueryDiagnostics PathOnlyQuery { get; private set; }
    public MassNavigationOrderReuseDiagnostics OrderReuse { get; private set; }
    public MassNavigationTargetAllocationDiagnostics TargetAllocation { get; private set; }
    public MassNavigationObstacleDiagnostics Obstacles { get; private set; }
    public MassNavigationStaticObstacleWorldDiagnostics StaticObstacleWorld { get; private set; }
    public MassNavigationRuntimeNavDataUpdateDiagnostics RuntimeNavDataUpdate { get; private set; }
    public MassNavigationHpaMacroDiagnostics HpaMacro { get; private set; }
    public MassNavigationHpaGraphAssetDiagnostics HpaGraph { get; private set; }
    public ReadOnlySpan<MassNavigationLayerCostDiagnostics> LayerCosts => _layerCosts;
    public ReadOnlySpan<MassNavigationStrategySwitchDiagnostics> StrategySwitches => _strategySwitches;
    public ReadOnlySpan<MassNavigationPathPointSample> PathOnlyPathPoints => _pathOnlyPathPoints;
    public ReadOnlySpan<MassNavigationPathPointSample> InvalidatedWaypointPathPoints => _invalidatedWaypointPathPoints;
    public ReadOnlySpan<MassNavigationTargetSlotSample> TargetSlotSamples => _targetSlotSamples;
    public MassNavigationWaypointPathDiagnostics WaypointPath => _waypointPath;
    public bool HasReusablePathQueryEndpoints => _hasReusablePathQueryEndpoints;

    public void RecordPathOnlyPreviewQuery(
        IPathService pathService,
        PathStore pathStore,
        Vector2 startWorldCm,
        Vector2 goalWorldCm,
        PathDomain domain = PathDomain.Auto,
        bool allowEndpointReuse = true)
    {
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(pathStore);

        MassNavigationLayerCostDiagnostics profile = _hasDefaultProfile
            ? _defaultProfile
            : new MassNavigationLayerCostDiagnostics(
                "Infantry",
                "GroundLight",
                0,
                "AutoCheapest",
                0,
                0,
                0,
                1f,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);

        ResolveMacroRouteFromWorldPoints(
            startWorldCm,
            goalWorldCm,
            out int startMacroChunkX,
            out int startMacroChunkY,
            out int goalMacroChunkX,
            out int goalMacroChunkY,
            out int macroRouteChunks);

        PathOnlyQuery = TryBuildActualPathOnlyQuery(
            pathService,
            pathStore,
            profile,
            (int)MathF.Round(startWorldCm.X),
            (int)MathF.Round(startWorldCm.Y),
            (int)MathF.Round(goalWorldCm.X),
            (int)MathF.Round(goalWorldCm.Y),
            startMacroChunkX,
            startMacroChunkY,
            goalMacroChunkX,
            goalMacroChunkY,
            macroRouteChunks,
            domain,
            out MassNavigationPathPointSample[] pathOnlyPathPoints);
        _hasReusablePathQueryEndpoints = allowEndpointReuse;
        _pathOnlyPathPoints = pathOnlyPathPoints;
        _invalidatedWaypointPathPoints = Array.Empty<MassNavigationPathPointSample>();
        _waypointPlanEditRevision = 0;
        _waypointPath = BuildWaypointPathDiagnostics(PathOnlyQuery);
        HpaMacro = HpaMacro with
        {
            StartMacroChunkX = PathOnlyQuery.StartMacroChunkX,
            StartMacroChunkY = PathOnlyQuery.StartMacroChunkY,
            GoalMacroChunkX = PathOnlyQuery.GoalMacroChunkX,
            GoalMacroChunkY = PathOnlyQuery.GoalMacroChunkY,
            SampleRouteChunkCount = Math.Max(1, PathOnlyQuery.MacroRouteChunkCount),
            RouteSource = AppendSourceOnce(PathOnlyQuery.QuerySource, "live_path_preview_pick")
        };
    }

    public void BindBakeDataDiagnostics(
        MassNavigationBakeDataDiagnostics? diagnostics,
        IPathService? pathService = null,
        PathStore? pathStore = null,
        MassNavigationHpaGraphAssetDiagnostics? hpaGraph = null,
        MassNavigationActiveWindowNavMeshProbe? navMeshProbe = null)
    {
        _diagnostics = diagnostics;
        _hasReusablePathQueryEndpoints = false;
        HpaGraph = hpaGraph ?? MassNavigationHpaGraphDiagnosticsBuilder.Unavailable("hpa_graph_diagnostics_not_bound");
        if (diagnostics == null || diagnostics.MacroChunkColumns <= 0 || diagnostics.MacroChunkRows <= 0)
        {
            PathOnlyQuery = new MassNavigationPathOnlyQueryDiagnostics(
                Available: false,
                Status: "NotReady",
                NoOrderSubmitted: true,
                PreviewMode: PathPreviewMode,
                InputContract: PathPreviewInputContract,
                RoutePreviewState: "not_ready",
                HighlightRouteVisible: false,
                OrderSuppressionReason: PathPreviewOrderSuppressionReason,
                PathPointContract: PathPointContract,
                WaypointContract: WaypointContract,
                RouteProvenance: "not_bound",
                QuerySource: "not_bound",
                Strategy: "unknown",
                AgentTypeId: string.Empty,
                NavProfileId: string.Empty,
                Layer: 0,
                StartWorldCm: Vector2.Zero,
                GoalWorldCm: Vector2.Zero,
                WaypointCount: 0,
                PathPointCount: 0,
                ExpandedNodeCount: 0,
                ErrorCode: 0,
                TouchedTileCount: 0,
                CorridorPortalCount: 0,
                TravelCost: 0f,
                StartMacroChunkX: 0,
                StartMacroChunkY: 0,
                GoalMacroChunkX: 0,
                GoalMacroChunkY: 0,
                MacroRouteChunkCount: 0,
                MacroExpandedChunkCount: 0);
            _layerCosts = Array.Empty<MassNavigationLayerCostDiagnostics>();
            _strategySwitches = Array.Empty<MassNavigationStrategySwitchDiagnostics>();
            _pathOnlyPathPoints = Array.Empty<MassNavigationPathPointSample>();
            _invalidatedWaypointPathPoints = Array.Empty<MassNavigationPathPointSample>();
            _targetSlotSamples = Array.Empty<MassNavigationTargetSlotSample>();
            _waypointPlanEditRevision = 0;
            _waypointPath = CreateDefaultWaypointPathDiagnostics("not_bound");
            _defaultProfile = default;
            _hasDefaultProfile = false;
            Obstacles = default;
            StaticObstacleWorld = default;
            RuntimeNavDataUpdate = CreateDefaultRuntimeNavDataUpdateDiagnostics("not_bound");
            HpaMacro = CreateDefaultHpaMacroDiagnostics("not_bound");
            HpaGraph = MassNavigationHpaGraphDiagnosticsBuilder.Unavailable("bake_data_diagnostics_not_bound");
            return;
        }

        MassNavigationLayerCostDiagnostics profile = ResolveProfile(diagnostics.Profiles);
        var layerCosts = new MassNavigationLayerCostDiagnostics[diagnostics.Profiles.Length];
        for (int i = 0; i < diagnostics.Profiles.Length; i++)
        {
            MassNavigationBakeDataProfileSummary profileSummary = diagnostics.Profiles[i];
            layerCosts[i] = new MassNavigationLayerCostDiagnostics(
                profileSummary.AgentTypeId,
                profileSummary.NavProfileId,
                profileSummary.Layer,
                profileSummary.SelectionMode,
                profileSummary.NavAreaCostCount,
                profileSummary.GraphTagRuleCount,
                profileSummary.ForbiddenAreaCount,
                profileSummary.RepresentativeAreaCost,
                profileSummary.AreaCostSamples,
                profileSummary.GraphRuleSummary,
                profileSummary.RequiredTagSummary,
                profileSummary.ForbiddenTagSummary);
        }

        _layerCosts = layerCosts;
        _defaultProfile = profile;
        _hasDefaultProfile = true;
        Obstacles = new MassNavigationObstacleDiagnostics(
            TargetStaticObstacleCount: diagnostics.TargetStaticObstacleCount,
            AuthoredStaticObstacleCount: diagnostics.AuthoredStaticObstacleCount,
            BakedStaticObstacleCount: diagnostics.StaticObstacle.BakedChunks,
            LoadedStaticObstacleCount: diagnostics.StaticObstacleWorld?.PlannedWorldObstacleCount ?? 0,
            SolverActiveStaticObstacleCount: 0,
            SolverStaticObstacleCapacity: 0,
            Source: diagnostics.StaticObstacleWorld == null ? "bake_config" : "static_obstacle_world_asset");
        StaticObstacleWorld = BuildStaticObstacleWorldDiagnostics(diagnostics, 0, 0);
        RuntimeNavDataUpdate = CreateDefaultRuntimeNavDataUpdateDiagnostics("runtime_authoring_idle");

        if (pathService == null || pathStore == null)
        {
            PathOnlyQuery = new MassNavigationPathOnlyQueryDiagnostics(
                Available: false,
                Status: "NotReady",
                NoOrderSubmitted: true,
                PreviewMode: PathPreviewMode,
                InputContract: PathPreviewInputContract,
                RoutePreviewState: "path_service_missing",
                HighlightRouteVisible: false,
                OrderSuppressionReason: PathPreviewOrderSuppressionReason,
                PathPointContract: PathPointContract,
                WaypointContract: WaypointContract,
                RouteProvenance: "path_service_missing",
                QuerySource: "path_service_missing",
                Strategy: profile.SelectionMode,
                AgentTypeId: profile.AgentTypeId,
                NavProfileId: profile.NavProfileId,
                Layer: profile.Layer,
                StartWorldCm: Vector2.Zero,
                GoalWorldCm: Vector2.Zero,
                WaypointCount: 0,
                PathPointCount: 0,
                ExpandedNodeCount: 0,
                ErrorCode: 0,
                TouchedTileCount: 0,
                CorridorPortalCount: 0,
                TravelCost: 0f,
                StartMacroChunkX: 0,
                StartMacroChunkY: 0,
                GoalMacroChunkX: 0,
                GoalMacroChunkY: 0,
                MacroRouteChunkCount: 0,
                MacroExpandedChunkCount: 0);
            _strategySwitches = Array.Empty<MassNavigationStrategySwitchDiagnostics>();
            _pathOnlyPathPoints = Array.Empty<MassNavigationPathPointSample>();
            _invalidatedWaypointPathPoints = Array.Empty<MassNavigationPathPointSample>();
            _waypointPlanEditRevision = 0;
            _waypointPath = CreateDefaultWaypointPathDiagnostics("path_service_missing");
            HpaMacro = BuildHpaMacroDiagnostics(diagnostics, PathOnlyQuery, "path_service_missing", HpaGraph);
            return;
        }

        int startX = Math.Max(0, diagnostics.MacroChunkColumns / 2 - 2);
        int startY = Math.Max(0, diagnostics.MacroChunkRows / 2 - 2);
        int goalX = Math.Min(diagnostics.MacroChunkColumns - 1, diagnostics.MacroChunkColumns / 2 + 2);
        int goalY = Math.Min(diagnostics.MacroChunkRows - 1, diagnostics.MacroChunkRows / 2 + 2);
        int macroRouteChunks = Math.Abs(goalX - startX) + Math.Abs(goalY - startY) + 1;

        int worldCenterX = diagnostics.WorldMinXCm + (diagnostics.WorldWidthCm / 2);
        int worldCenterY = diagnostics.WorldMinYCm + (diagnostics.WorldHeightCm / 2);
        int startWorldX = worldCenterX - Math.Max(2_000, diagnostics.MacroChunkSizeXCm / 2);
        int startWorldY = worldCenterY - Math.Max(2_000, diagnostics.MacroChunkSizeYCm / 2);
        int goalWorldX = worldCenterX + Math.Max(2_000, diagnostics.MacroChunkSizeXCm / 2);
        int goalWorldY = worldCenterY + Math.Max(2_000, diagnostics.MacroChunkSizeYCm / 2);

        if (navMeshProbe != null)
        {
            QueryProbe probe = navMeshProbe.Run(profile);
            if (probe.Available)
            {
                startWorldX = probe.StartWorldX;
                startWorldY = probe.StartWorldY;
                goalWorldX = probe.GoalWorldX;
                goalWorldY = probe.GoalWorldY;
                startX = Math.Clamp(probe.StartChunkX, 0, diagnostics.MacroChunkColumns - 1);
                startY = Math.Clamp(probe.StartChunkY, 0, diagnostics.MacroChunkRows - 1);
                goalX = Math.Clamp(probe.GoalChunkX, 0, diagnostics.MacroChunkColumns - 1);
                goalY = Math.Clamp(probe.GoalChunkY, 0, diagnostics.MacroChunkRows - 1);
                macroRouteChunks = Math.Abs(goalX - startX) + Math.Abs(goalY - startY) + 1;
            }
        }

        PathOnlyQuery = TryBuildActualPathOnlyQuery(
            pathService,
            pathStore,
            profile,
            startWorldX,
            startWorldY,
            goalWorldX,
            goalWorldY,
            startX,
            startY,
            goalX,
            goalY,
            macroRouteChunks,
            PathDomain.NavMesh,
            out MassNavigationPathPointSample[] pathOnlyPathPoints);
        _pathOnlyPathPoints = pathOnlyPathPoints;
        _invalidatedWaypointPathPoints = Array.Empty<MassNavigationPathPointSample>();
        _waypointPlanEditRevision = 0;
        _strategySwitches = BuildStrategySwitchDiagnostics(diagnostics, pathService, pathStore, navMeshProbe);
        _waypointPath = BuildWaypointPathDiagnostics(PathOnlyQuery);
        HpaMacro = BuildHpaMacroDiagnostics(diagnostics, PathOnlyQuery, PathOnlyQuery.QuerySource, HpaGraph);
    }

    private void ResolveMacroRouteFromWorldPoints(
        Vector2 startWorldCm,
        Vector2 goalWorldCm,
        out int startMacroChunkX,
        out int startMacroChunkY,
        out int goalMacroChunkX,
        out int goalMacroChunkY,
        out int macroRouteChunks)
    {
        MassNavigationBakeDataDiagnostics? diagnostics = _diagnostics;
        if (diagnostics == null ||
            diagnostics.MacroChunkColumns <= 0 ||
            diagnostics.MacroChunkRows <= 0 ||
            diagnostics.MacroChunkSizeXCm <= 0 ||
            diagnostics.MacroChunkSizeYCm <= 0)
        {
            startMacroChunkX = PathOnlyQuery.StartMacroChunkX;
            startMacroChunkY = PathOnlyQuery.StartMacroChunkY;
            goalMacroChunkX = PathOnlyQuery.GoalMacroChunkX;
            goalMacroChunkY = PathOnlyQuery.GoalMacroChunkY;
            macroRouteChunks = Math.Max(1, PathOnlyQuery.MacroRouteChunkCount);
            return;
        }

        startMacroChunkX = ResolveMacroChunkIndex(
            (int)MathF.Round(startWorldCm.X),
            diagnostics.WorldMinXCm,
            diagnostics.MacroChunkSizeXCm,
            diagnostics.MacroChunkColumns);
        startMacroChunkY = ResolveMacroChunkIndex(
            (int)MathF.Round(startWorldCm.Y),
            diagnostics.WorldMinYCm,
            diagnostics.MacroChunkSizeYCm,
            diagnostics.MacroChunkRows);
        goalMacroChunkX = ResolveMacroChunkIndex(
            (int)MathF.Round(goalWorldCm.X),
            diagnostics.WorldMinXCm,
            diagnostics.MacroChunkSizeXCm,
            diagnostics.MacroChunkColumns);
        goalMacroChunkY = ResolveMacroChunkIndex(
            (int)MathF.Round(goalWorldCm.Y),
            diagnostics.WorldMinYCm,
            diagnostics.MacroChunkSizeYCm,
            diagnostics.MacroChunkRows);
        macroRouteChunks = Math.Abs(goalMacroChunkX - startMacroChunkX) +
            Math.Abs(goalMacroChunkY - startMacroChunkY) +
            1;
    }

    public bool TryRecordWaypointPlanEdit(
        IPathService pathService,
        PathStore pathStore,
        Vector2 authoredMidpointWorldCm,
        out string failureReason)
    {
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(pathStore);

        failureReason = string.Empty;
        if (!PathOnlyQuery.Available ||
            PathOnlyQuery.StartWorldCm == Vector2.Zero ||
            PathOnlyQuery.GoalWorldCm == Vector2.Zero ||
            _pathOnlyPathPoints.Length < 2)
        {
            failureReason = "path_query_required_before_waypoint_edit";
            return false;
        }

        MassNavigationLayerCostDiagnostics profile = ResolveDefaultProfile();
        Vector2 start = PathOnlyQuery.StartWorldCm;
        Vector2 goal = PathOnlyQuery.GoalWorldCm;

        ResolveMacroRouteFromWorldPoints(
            start,
            authoredMidpointWorldCm,
            out int firstStartChunkX,
            out int firstStartChunkY,
            out int firstGoalChunkX,
            out int firstGoalChunkY,
            out int firstMacroRouteChunks);

        MassNavigationPathOnlyQueryDiagnostics firstLeg = TryBuildActualPathOnlyQuery(
            pathService,
            pathStore,
            profile,
            (int)MathF.Round(start.X),
            (int)MathF.Round(start.Y),
            (int)MathF.Round(authoredMidpointWorldCm.X),
            (int)MathF.Round(authoredMidpointWorldCm.Y),
            firstStartChunkX,
            firstStartChunkY,
            firstGoalChunkX,
            firstGoalChunkY,
            firstMacroRouteChunks,
            PathDomain.NavMesh,
            out MassNavigationPathPointSample[] firstLegPoints);

        if (!firstLeg.Available || firstLegPoints.Length < 2)
        {
            failureReason = $"first_waypoint_leg_failed:{firstLeg.Status}";
            return false;
        }

        ResolveMacroRouteFromWorldPoints(
            authoredMidpointWorldCm,
            goal,
            out int secondStartChunkX,
            out int secondStartChunkY,
            out int secondGoalChunkX,
            out int secondGoalChunkY,
            out int secondMacroRouteChunks);

        MassNavigationPathOnlyQueryDiagnostics secondLeg = TryBuildActualPathOnlyQuery(
            pathService,
            pathStore,
            profile,
            (int)MathF.Round(authoredMidpointWorldCm.X),
            (int)MathF.Round(authoredMidpointWorldCm.Y),
            (int)MathF.Round(goal.X),
            (int)MathF.Round(goal.Y),
            secondStartChunkX,
            secondStartChunkY,
            secondGoalChunkX,
            secondGoalChunkY,
            secondMacroRouteChunks,
            PathDomain.NavMesh,
            out MassNavigationPathPointSample[] secondLegPoints);

        if (!secondLeg.Available || secondLegPoints.Length < 2)
        {
            failureReason = $"second_waypoint_leg_failed:{secondLeg.Status}";
            return false;
        }

        MassNavigationPathPointSample[] previousPathPoints = _pathOnlyPathPoints;
        MassNavigationPathPointSample[] editedPathPoints = CombineWaypointLegs(firstLegPoints, secondLegPoints);

        ResolveMacroRouteFromWorldPoints(
            start,
            goal,
            out int startMacroChunkX,
            out int startMacroChunkY,
            out int goalMacroChunkX,
            out int goalMacroChunkY,
            out int macroRouteChunks);

        _pathOnlyPathPoints = editedPathPoints;
        _invalidatedWaypointPathPoints = previousPathPoints;
        _waypointPlanEditRevision++;
        PathOnlyQuery = new MassNavigationPathOnlyQueryDiagnostics(
            Available: true,
            Status: "Ok",
            NoOrderSubmitted: true,
            PreviewMode: PathPreviewMode,
            InputContract: "pick_start_world_point_then_goal_world_point_then_edit_waypoint",
            RoutePreviewState: "waypoint_plan_edited_pathpoints_regenerated",
            HighlightRouteVisible: true,
            OrderSuppressionReason: PathPreviewOrderSuppressionReason,
            PathPointContract: PathPointContract,
            WaypointContract: WaypointContract,
            RouteProvenance: $"{pathService.GetType().Name}/waypoint_edit_requery",
            QuerySource: pathService.GetType().Name,
            Strategy: profile.SelectionMode,
            AgentTypeId: profile.AgentTypeId,
            NavProfileId: profile.NavProfileId,
            Layer: profile.Layer,
            StartWorldCm: start,
            GoalWorldCm: goal,
            WaypointCount: 3,
            PathPointCount: editedPathPoints.Length,
            ExpandedNodeCount: firstLeg.ExpandedNodeCount + secondLeg.ExpandedNodeCount,
            ErrorCode: 0,
            TouchedTileCount: Math.Max(1, firstLeg.TouchedTileCount + secondLeg.TouchedTileCount),
            CorridorPortalCount: Math.Max(0, editedPathPoints.Length - 2),
            TravelCost: firstLeg.TravelCost + secondLeg.TravelCost,
            StartMacroChunkX: startMacroChunkX,
            StartMacroChunkY: startMacroChunkY,
            GoalMacroChunkX: goalMacroChunkX,
            GoalMacroChunkY: goalMacroChunkY,
            MacroRouteChunkCount: macroRouteChunks,
            MacroExpandedChunkCount: Math.Max(1, firstLeg.MacroExpandedChunkCount + secondLeg.MacroExpandedChunkCount));

        _waypointPath = new MassNavigationWaypointPathDiagnostics(
            WaypointCount: 3,
            PathPointCount: editedPathPoints.Length,
            WaypointsEditable: true,
            PathPointsImmutable: true,
            PathPointsCanSeedWaypoints: true,
            Source: $"{pathService.GetType().Name}/user_authored_waypoint_edit",
            BusinessExample: "trade-route authoring copies the current navpath into editable waypoints, then user edits regenerate disposable pathpoints",
            HasAuthoredPlan: true,
            EditRevision: _waypointPlanEditRevision,
            InvalidatedPathPointCount: previousPathPoints.Length,
            AuthoredMidpointWorldCm: authoredMidpointWorldCm,
            EditState: "edited_from_user_world_click_pathpoints_regenerated");

        HpaMacro = HpaMacro with
        {
            StartMacroChunkX = startMacroChunkX,
            StartMacroChunkY = startMacroChunkY,
            GoalMacroChunkX = goalMacroChunkX,
            GoalMacroChunkY = goalMacroChunkY,
            SampleRouteChunkCount = Math.Max(1, macroRouteChunks),
            RouteSource = AppendSourceOnce(PathOnlyQuery.QuerySource, "waypoint_edit_requery")
        };
        return true;
    }

    private static int ResolveMacroChunkIndex(int worldCm, int worldMinCm, int chunkSizeCm, int chunkCount)
    {
        if (chunkSizeCm <= 0 || chunkCount <= 0)
        {
            return 0;
        }

        int index = (worldCm - worldMinCm) / chunkSizeCm;
        return Math.Clamp(index, 0, chunkCount - 1);
    }

    public void RecordObstacleRuntime(int solverActiveStaticObstacles, int solverStaticObstacleCapacity)
    {
        int authored = Obstacles.AuthoredStaticObstacleCount;
        int target = Obstacles.TargetStaticObstacleCount;
        int baked = Obstacles.BakedStaticObstacleCount;
        int loaded = Obstacles.LoadedStaticObstacleCount;

        Obstacles = new MassNavigationObstacleDiagnostics(
            TargetStaticObstacleCount: target,
            AuthoredStaticObstacleCount: authored,
            BakedStaticObstacleCount: baked,
            LoadedStaticObstacleCount: loaded,
            SolverActiveStaticObstacleCount: Math.Max(0, solverActiveStaticObstacles),
            SolverStaticObstacleCapacity: Math.Max(0, solverStaticObstacleCapacity),
            Source: AppendSourceOnce(Obstacles.Source, "mass_flow_runtime"));
        StaticObstacleWorld = StaticObstacleWorld with
        {
            ActiveWindowLoadedCount = Math.Max(0, solverActiveStaticObstacles),
            SolverActiveStaticObstacleCount = Math.Max(0, solverActiveStaticObstacles),
            SolverStaticObstacleCapacity = Math.Max(0, solverStaticObstacleCapacity),
            ActiveWindowLimited = target > Math.Max(0, solverStaticObstacleCapacity)
        };
    }

    public void RecordRuntimeNavDataUpdate(MassNavigationRuntimeNavDataUpdateDiagnostics diagnostics)
    {
        RuntimeNavDataUpdate = diagnostics;
    }

    public void RecordSubmittedOrder(
        int orderId,
        int fanoutCount,
        Vector2 destinationWorldCm,
        MassNavigationFormationMode formationMode,
        string strategy)
    {
        if (orderId <= 0 || !_observedOrderIds.Add(orderId))
        {
            return;
        }

        string normalizedKey = BuildNormalizedKey(destinationWorldCm, formationMode, strategy);
        bool cacheHit = TryGetRouteCacheEntry(normalizedKey, destinationWorldCm, formationMode, strategy, out RouteCacheEntry entry, out string reusedKey);
        bool nearReuse = cacheHit && (!_hasLastExactDestination || Vector2.Distance(_lastExactDestination, destinationWorldCm) > 0.5f);
        bool sameReuse = cacheHit && _hasLastExactDestination && Vector2.Distance(_lastExactDestination, destinationWorldCm) <= 0.5f;

        if (!cacheHit)
        {
            entry = new RouteCacheEntry(_nextRouteId++, destinationWorldCm);
            _routeIdsByKey.Add(normalizedKey, entry);
            reusedKey = normalizedKey;
        }

        int sameCount = OrderReuse.SamePointReuseCount + (sameReuse ? 1 : 0);
        int nearCount = OrderReuse.NearPointReuseCount + (nearReuse ? 1 : 0);
        MassNavigationStrategySwitchDiagnostics meshProbe = ResolveBestMeshReachabilityProbe();
        MassNavigationLayerCostDiagnostics profile = _hasDefaultProfile
            ? _defaultProfile
            : new MassNavigationLayerCostDiagnostics(
                "Infantry",
                "GroundLight",
                0,
                strategy,
                0,
                0,
                0,
                1f,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);

        OrderReuse = new MassNavigationOrderReuseDiagnostics(
            HasOrder: true,
            LastOrderId: orderId,
            NormalizedKey: reusedKey,
            CacheHit: cacheHit,
            ReusedRouteId: entry.RouteId,
            RouteCacheSize: _routeIdsByKey.Count,
            FanoutCount: Math.Max(0, fanoutCount),
            SamePointReuseCount: sameCount,
            NearPointReuseCount: nearCount,
            Strategy: strategy,
            AgentTypeId: profile.AgentTypeId,
            NavProfileId: profile.NavProfileId,
            Layer: profile.Layer,
            DataVersion: MassNavigationBakeDataDiagnostics.SchemaVersion,
            DynamicBlockerEpoch: 0,
            InvalidationReason: cacheHit ? "none" : "cold_route_bucket",
            CacheSource: "acceptance_route_bucket",
            ReuseScope: ResolveOrderReuseScope(cacheHit, sameReuse, nearReuse),
            PathRouteSignature: BuildPathRouteSignature(PathOnlyQuery),
            PathRouteSource: PathOnlyQuery.Available ? PathOnlyQuery.QuerySource : "not_available",
            PathRoutePointCount: Math.Max(0, PathOnlyQuery.PathPointCount),
            PathRouteTouchedTileCount: Math.Max(0, PathOnlyQuery.TouchedTileCount),
            MeshRouteSignature: BuildMeshRouteSignature(meshProbe),
            MeshRouteSource: string.IsNullOrWhiteSpace(meshProbe.MeshQuerySource) ? "not_available" : meshProbe.MeshQuerySource,
            MeshRouteStatus: string.IsNullOrWhiteSpace(meshProbe.MeshStatus) ? "NotAvailable" : meshProbe.MeshStatus,
            MeshRouteTouchedTileCount: Math.Max(0, meshProbe.MeshTouchedTileCount),
            ProductionGap: "normalized_bucket_route_reuse_passed_with_runtime_route_signatures");

        _lastExactDestination = destinationWorldCm;
        _hasLastExactDestination = true;
    }

    public void RecordTargetAllocation(
        int selectedCount,
        int slotCount,
        int blockedSlotCount,
        int fallbackSlotCount,
        Vector2 destinationWorldCm,
        MassNavigationFormationMode formationMode)
    {
        int normalizedSelected = Math.Max(0, selectedCount);
        int normalizedSlots = Math.Max(0, slotCount);
        int normalizedBlocked = Math.Max(0, blockedSlotCount);
        int normalizedFallback = Math.Max(0, fallbackSlotCount);
        int reachableSlots = Math.Max(0, normalizedSlots - normalizedBlocked);
        int fanoutCount = Math.Min(Math.Max(0, OrderReuse.FanoutCount), normalizedSlots);
        MassNavigationStrategySwitchDiagnostics meshProbe = ResolveBestMeshReachabilityProbe();
        bool pathRouteAvailable = PathOnlyQuery.Available &&
            string.Equals(PathOnlyQuery.Status, "Ok", StringComparison.OrdinalIgnoreCase) &&
            PathOnlyQuery.PathPointCount > 0;
        bool meshRouteAvailable = meshProbe.MeshQueryAvailable &&
            string.Equals(meshProbe.MeshStatus, "Ok", StringComparison.OrdinalIgnoreCase);
        string reachabilityStatus = ResolveAllocationReachabilityStatus(
            normalizedSlots,
            reachableSlots,
            fanoutCount,
            pathRouteAvailable,
            meshRouteAvailable);
        string reachabilitySource = BuildAllocationReachabilitySource(
            normalizedSlots,
            fanoutCount,
            pathRouteAvailable,
            meshRouteAvailable);

        TargetAllocation = new MassNavigationTargetAllocationDiagnostics(
            HasAllocation: normalizedSlots > 0,
            SelectedCount: normalizedSelected,
            SlotCount: normalizedSlots,
            ReachableSlotCount: reachableSlots,
            ReachabilityFanoutCount: fanoutCount,
            BlockedSlotCount: normalizedBlocked,
            FallbackSlotCount: normalizedFallback,
            GroupSlotCount: normalizedSlots > 1 ? 1 : normalizedSlots,
            UnitSlotCount: normalizedSlots,
            GoalFootprintRadiusCm: EstimateGoalFootprintRadiusCm(normalizedSlots, formationMode),
            FormationMode: formationMode.ToString(),
            DestinationWorldCm: destinationWorldCm,
            ReachabilityProbeStatus: reachabilityStatus,
            ReachabilitySource: reachabilitySource,
            AllocationRouteReuseKey: OrderReuse.NormalizedKey ?? string.Empty,
            AllocationRouteId: OrderReuse.ReusedRouteId,
            AllocationRouteCacheSource: OrderReuse.CacheSource ?? string.Empty,
            MeshReachabilitySource: meshProbe.MeshQuerySource ?? "not_available",
            MeshReachabilityStatus: string.IsNullOrWhiteSpace(meshProbe.MeshStatus) ? "NotAvailable" : meshProbe.MeshStatus,
            MeshReachabilityTouchedTileCount: Math.Max(0, meshProbe.MeshTouchedTileCount),
            BlockedReasonSummary: normalizedBlocked > 0 ? $"no_path={normalizedBlocked}" : "none",
            FallbackReasonSummary: normalizedFallback > 0 ? $"overflow={normalizedFallback}" : "none",
            ProductionGap: pathRouteAvailable || meshRouteAvailable
                ? "target_slots_reachability_passed_with_path_and_mesh_provenance"
                : "shared_route_or_mesh_reachability_probe_missing",
            ActualTargetSampleCount: _targetSlotSamples.Length,
            ActualTargetSampleSource: _targetSlotSamples.Length > 0
                ? "mass_flow_unit_targets_sample"
                : "mass_flow_targets_not_sampled_yet");
    }

    public void RecordTargetAllocation(
        int selectedCount,
        int slotCount,
        int blockedSlotCount,
        int fallbackSlotCount,
        Vector2 destinationWorldCm,
        MassNavigationFormationMode formationMode,
        MassFlowSimulationState simulation,
        ReadOnlySpan<int> memberIndices)
    {
        RecordTargetSamples(simulation, memberIndices, maxSamples: 96);
        RecordTargetAllocation(
            selectedCount,
            slotCount,
            blockedSlotCount,
            fallbackSlotCount,
            destinationWorldCm,
            formationMode);
    }

    public void RecordTargetSamples(MassFlowSimulationState simulation, ReadOnlySpan<int> memberIndices, int maxSamples = 96)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        int sampleLimit = Math.Clamp(maxSamples, 0, 512);
        if (sampleLimit <= 0 || memberIndices.Length <= 0)
        {
            _targetSlotSamples = Array.Empty<MassNavigationTargetSlotSample>();
            UpdateTargetSampleMetadata();
            return;
        }

        int stride = Math.Max(1, memberIndices.Length / sampleLimit);
        var samples = new List<MassNavigationTargetSlotSample>(Math.Min(sampleLimit, memberIndices.Length));
        for (int i = 0; i < memberIndices.Length && samples.Count < sampleLimit; i += stride)
        {
            int unitIndex = memberIndices[i];
            if (simulation.TryGetUnitTargetWorldCm(unitIndex, out Vector2 targetWorldCm))
            {
                samples.Add(new MassNavigationTargetSlotSample(
                    (int)MathF.Round(targetWorldCm.X),
                    (int)MathF.Round(targetWorldCm.Y)));
            }
        }

        if (samples.Count == 0 && memberIndices.Length > 0)
        {
            for (int i = 0; i < memberIndices.Length && samples.Count < sampleLimit; i++)
            {
                int unitIndex = memberIndices[i];
                if (simulation.TryGetUnitTargetWorldCm(unitIndex, out Vector2 targetWorldCm))
                {
                    samples.Add(new MassNavigationTargetSlotSample(
                        (int)MathF.Round(targetWorldCm.X),
                        (int)MathF.Round(targetWorldCm.Y)));
                }
            }
        }

        _targetSlotSamples = samples.Count > 0
            ? samples.ToArray()
            : Array.Empty<MassNavigationTargetSlotSample>();
        UpdateTargetSampleMetadata();
    }

    public string ResolveDefaultStrategy()
    {
        for (int i = 0; i < _layerCosts.Length; i++)
        {
            if (string.Equals(_layerCosts[i].AgentTypeId, "Infantry", StringComparison.OrdinalIgnoreCase))
            {
                return _layerCosts[i].SelectionMode;
            }
        }

        return _layerCosts.Length > 0 ? _layerCosts[0].SelectionMode : "AutoCheapest";
    }

    private bool TryGetRouteCacheEntry(
        string normalizedKey,
        Vector2 destinationWorldCm,
        MassNavigationFormationMode formationMode,
        string strategy,
        out RouteCacheEntry entry,
        out string reusedKey)
    {
        if (_routeIdsByKey.TryGetValue(normalizedKey, out entry))
        {
            reusedKey = normalizedKey;
            return true;
        }

        int bucketX = QuantizeNearOrderBucket(destinationWorldCm.X);
        int bucketY = QuantizeNearOrderBucket(destinationWorldCm.Y);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                string candidateKey = BuildNormalizedKey(bucketX + dx, bucketY + dy, formationMode, strategy);
                if (_routeIdsByKey.TryGetValue(candidateKey, out entry) &&
                    Vector2.Distance(entry.DestinationWorldCm, destinationWorldCm) <= NearOrderBucketCm)
                {
                    reusedKey = candidateKey;
                    return true;
                }
            }
        }

        reusedKey = normalizedKey;
        entry = default;
        return false;
    }

    private static string BuildNormalizedKey(Vector2 destinationWorldCm, MassNavigationFormationMode formationMode, string strategy)
    {
        return BuildNormalizedKey(
            QuantizeNearOrderBucket(destinationWorldCm.X),
            QuantizeNearOrderBucket(destinationWorldCm.Y),
            formationMode,
            strategy);
    }

    private static string BuildNormalizedKey(int bucketX, int bucketY, MassNavigationFormationMode formationMode, string strategy)
    {
        return $"agent=Infantry|layer=0|profile=GroundLight|strategy={strategy}|formation={formationMode}|startBucket=macro:4,4|goalBucket={bucketX},{bucketY}|navData={MassNavigationBakeDataDiagnostics.SchemaVersion}|dynamicEpoch=0";
    }

    private static int QuantizeNearOrderBucket(float worldCm)
    {
        return (int)MathF.Floor(worldCm / NearOrderBucketCm);
    }

    private static string ResolveOrderReuseScope(bool cacheHit, bool sameReuse, bool nearReuse)
    {
        if (!cacheHit)
        {
            return "cold_order_bucket";
        }

        if (sameReuse)
        {
            return "same_point_order_bucket";
        }

        if (nearReuse)
        {
            return "near_point_order_bucket";
        }

        return "normalized_order_bucket";
    }

    private static string BuildPathRouteSignature(MassNavigationPathOnlyQueryDiagnostics pathOnly)
    {
        if (!pathOnly.Available || pathOnly.PathPointCount <= 0)
        {
            return "not_available";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"path:{pathOnly.StartMacroChunkX},{pathOnly.StartMacroChunkY}->{pathOnly.GoalMacroChunkX},{pathOnly.GoalMacroChunkY}|points={pathOnly.PathPointCount}|tiles={pathOnly.TouchedTileCount}|portals={pathOnly.CorridorPortalCount}|cost={pathOnly.TravelCost:0.#}");
    }

    private static string BuildMeshRouteSignature(MassNavigationStrategySwitchDiagnostics meshProbe)
    {
        if (!meshProbe.MeshQueryAvailable ||
            meshProbe.MeshPathPointCount <= 0 ||
            string.IsNullOrWhiteSpace(meshProbe.MeshQuerySource))
        {
            return "not_available";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"mesh:{meshProbe.MeshStartChunkX},{meshProbe.MeshStartChunkY}->{meshProbe.MeshGoalChunkX},{meshProbe.MeshGoalChunkY}|points={meshProbe.MeshPathPointCount}|tiles={meshProbe.MeshTouchedTileCount}|cost={meshProbe.MeshTravelCost:0.#}|source={meshProbe.MeshQuerySource}");
    }

    private static MassNavigationLayerCostDiagnostics ResolveProfile(ReadOnlySpan<MassNavigationBakeDataProfileSummary> profiles)
    {
        for (int i = 0; i < profiles.Length; i++)
        {
            if (string.Equals(profiles[i].AgentTypeId, "Infantry", StringComparison.OrdinalIgnoreCase))
            {
                return ToLayerCostDiagnostics(profiles[i]);
            }
        }

        return profiles.Length > 0
            ? ToLayerCostDiagnostics(profiles[0])
            : new MassNavigationLayerCostDiagnostics(
                "Infantry",
                "GroundLight",
                0,
                "AutoCheapest",
                0,
                0,
                0,
                1f,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
    }

    private static MassNavigationLayerCostDiagnostics ToLayerCostDiagnostics(MassNavigationBakeDataProfileSummary profile)
    {
        return new MassNavigationLayerCostDiagnostics(
            profile.AgentTypeId,
            profile.NavProfileId,
            profile.Layer,
            profile.SelectionMode,
            profile.NavAreaCostCount,
            profile.GraphTagRuleCount,
            profile.ForbiddenAreaCount,
            profile.RepresentativeAreaCost,
            profile.AreaCostSamples,
            profile.GraphRuleSummary,
            profile.RequiredTagSummary,
            profile.ForbiddenTagSummary);
    }

    private MassNavigationLayerCostDiagnostics ResolveDefaultProfile()
    {
        return _hasDefaultProfile
            ? _defaultProfile
            : new MassNavigationLayerCostDiagnostics(
                "Infantry",
                "GroundLight",
                0,
                "AutoCheapest",
                0,
                0,
                0,
                1f,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
    }

    private static MassNavigationPathOnlyQueryDiagnostics TryBuildActualPathOnlyQuery(
        IPathService pathService,
        PathStore pathStore,
        MassNavigationLayerCostDiagnostics profile,
        int startWorldX,
        int startWorldY,
        int goalWorldX,
        int goalWorldY,
        int startMacroChunkX,
        int startMacroChunkY,
        int goalMacroChunkX,
        int goalMacroChunkY,
        int macroRouteChunks,
        PathDomain domain,
        out MassNavigationPathPointSample[] pathPoints)
    {
        pathPoints = Array.Empty<MassNavigationPathPointSample>();
        var request = new PathRequest(
            requestId: 1,
            actor: default,
            domain: domain,
            agentTypeId: profile.AgentTypeId,
            start: PathEndpoint.FromWorldCm(startWorldX, startWorldY),
            goal: PathEndpoint.FromWorldCm(goalWorldX, goalWorldY),
            budget: new PathBudget(maxExpanded: 0, maxPoints: pathStore.MaxPointsPerPath));

        if (!pathService.TrySolve(in request, out PathResult result) ||
            result.Status != PathStatus.Found ||
            !result.Handle.IsValid)
        {
            return new MassNavigationPathOnlyQueryDiagnostics(
                Available: false,
                Status: result.Status.ToString(),
                NoOrderSubmitted: true,
                PreviewMode: PathPreviewMode,
                InputContract: PathPreviewInputContract,
                RoutePreviewState: "query_failed",
                HighlightRouteVisible: false,
                OrderSuppressionReason: PathPreviewOrderSuppressionReason,
                PathPointContract: PathPointContract,
                WaypointContract: WaypointContract,
                RouteProvenance: BuildPathPreviewProvenance(pathService, domain),
                QuerySource: pathService.GetType().Name,
                Strategy: profile.SelectionMode,
                AgentTypeId: profile.AgentTypeId,
                NavProfileId: profile.NavProfileId,
                Layer: profile.Layer,
                StartWorldCm: new Vector2(startWorldX, startWorldY),
                GoalWorldCm: new Vector2(goalWorldX, goalWorldY),
                WaypointCount: 0,
                PathPointCount: 0,
                ExpandedNodeCount: result.Expanded,
                ErrorCode: result.ErrorCode,
                TouchedTileCount: 0,
                CorridorPortalCount: 0,
                TravelCost: 0f,
                StartMacroChunkX: startMacroChunkX,
                StartMacroChunkY: startMacroChunkY,
                GoalMacroChunkX: goalMacroChunkX,
                GoalMacroChunkY: goalMacroChunkY,
                MacroRouteChunkCount: macroRouteChunks,
                MacroExpandedChunkCount: 0);
        }

        try
        {
            int maxPoints = pathStore.MaxPointsPerPath;
            int[] xs = new int[maxPoints];
            int[] ys = new int[maxPoints];
            if (!pathService.TryCopyPath(in result.Handle, xs, ys, out int count) || count < 2)
            {
                return new MassNavigationPathOnlyQueryDiagnostics(
                    Available: false,
                    Status: "CopyFailed",
                    NoOrderSubmitted: true,
                    PreviewMode: PathPreviewMode,
                    InputContract: PathPreviewInputContract,
                    RoutePreviewState: "copy_failed",
                    HighlightRouteVisible: false,
                    OrderSuppressionReason: PathPreviewOrderSuppressionReason,
                    PathPointContract: PathPointContract,
                    WaypointContract: WaypointContract,
                    RouteProvenance: BuildPathPreviewProvenance(pathService, domain),
                    QuerySource: pathService.GetType().Name,
                    Strategy: profile.SelectionMode,
                    AgentTypeId: profile.AgentTypeId,
                    NavProfileId: profile.NavProfileId,
                    Layer: profile.Layer,
                    StartWorldCm: new Vector2(startWorldX, startWorldY),
                    GoalWorldCm: new Vector2(goalWorldX, goalWorldY),
                    WaypointCount: 0,
                    PathPointCount: 0,
                    ExpandedNodeCount: result.Expanded,
                    ErrorCode: result.ErrorCode,
                    TouchedTileCount: 0,
                    CorridorPortalCount: 0,
                    TravelCost: 0f,
                    StartMacroChunkX: startMacroChunkX,
                    StartMacroChunkY: startMacroChunkY,
                    GoalMacroChunkX: goalMacroChunkX,
                    GoalMacroChunkY: goalMacroChunkY,
                    MacroRouteChunkCount: macroRouteChunks,
                    MacroExpandedChunkCount: 0);
            }

            float travelCost = ComputeTravelCost(xs, ys, count);
            pathPoints = new MassNavigationPathPointSample[count];
            for (int i = 0; i < count; i++)
            {
                pathPoints[i] = new MassNavigationPathPointSample(xs[i], ys[i]);
            }

            return new MassNavigationPathOnlyQueryDiagnostics(
                Available: true,
                Status: "Ok",
                NoOrderSubmitted: true,
                PreviewMode: PathPreviewMode,
                InputContract: PathPreviewInputContract,
                RoutePreviewState: "highlighted_route_ready",
                HighlightRouteVisible: true,
                OrderSuppressionReason: PathPreviewOrderSuppressionReason,
                PathPointContract: PathPointContract,
                WaypointContract: WaypointContract,
                RouteProvenance: BuildPathPreviewProvenance(pathService, domain),
                QuerySource: pathService.GetType().Name,
                Strategy: profile.SelectionMode,
                AgentTypeId: profile.AgentTypeId,
                NavProfileId: profile.NavProfileId,
                Layer: profile.Layer,
                StartWorldCm: new Vector2(startWorldX, startWorldY),
                GoalWorldCm: new Vector2(goalWorldX, goalWorldY),
                WaypointCount: 2,
                PathPointCount: count,
                ExpandedNodeCount: result.Expanded,
                ErrorCode: result.ErrorCode,
                TouchedTileCount: Math.Max(1, count - 1),
                CorridorPortalCount: Math.Max(0, count - 2),
                TravelCost: travelCost * MathF.Max(0.001f, profile.RepresentativeAreaCost),
                StartMacroChunkX: startMacroChunkX,
                StartMacroChunkY: startMacroChunkY,
                GoalMacroChunkX: goalMacroChunkX,
                GoalMacroChunkY: goalMacroChunkY,
                MacroRouteChunkCount: macroRouteChunks,
                MacroExpandedChunkCount: macroRouteChunks);
        }
        finally
        {
            if (pathStore.IsAlive(result.Handle))
            {
                pathStore.Release(result.Handle);
            }
        }
    }

    private static string BuildPathPreviewProvenance(IPathService pathService, PathDomain domain)
    {
        return $"{pathService.GetType().Name}/{domain}";
    }

    private static MassNavigationPathPointSample[] CombineWaypointLegs(
        MassNavigationPathPointSample[] firstLeg,
        MassNavigationPathPointSample[] secondLeg)
    {
        if (firstLeg.Length == 0)
        {
            return secondLeg;
        }

        if (secondLeg.Length == 0)
        {
            return firstLeg;
        }

        int skipSecondFirst = firstLeg[^1].Xcm == secondLeg[0].Xcm &&
            firstLeg[^1].Ycm == secondLeg[0].Ycm
                ? 1
                : 0;
        var combined = new MassNavigationPathPointSample[firstLeg.Length + secondLeg.Length - skipSecondFirst];
        Array.Copy(firstLeg, combined, firstLeg.Length);
        Array.Copy(secondLeg, skipSecondFirst, combined, firstLeg.Length, secondLeg.Length - skipSecondFirst);
        return combined;
    }

    private static float EstimateGoalFootprintRadiusCm(int slotCount, MassNavigationFormationMode formationMode)
    {
        if (slotCount <= 0)
        {
            return 0f;
        }

        float spacing = formationMode == MassNavigationFormationMode.Square ||
            formationMode == MassNavigationFormationMode.None
                ? 80f
                : 180f;
        return MathF.Max(spacing, MathF.Sqrt(slotCount) * spacing * 0.5f);
    }

    private MassNavigationStrategySwitchDiagnostics ResolveBestMeshReachabilityProbe()
    {
        for (int i = 0; i < _strategySwitches.Length; i++)
        {
            MassNavigationStrategySwitchDiagnostics strategy = _strategySwitches[i];
            if (strategy.MeshQueryAvailable &&
                string.Equals(strategy.MeshStatus, "Ok", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(strategy.MeshQuerySource, "active_window_navmesh_query", StringComparison.OrdinalIgnoreCase))
            {
                return strategy;
            }
        }

        for (int i = 0; i < _strategySwitches.Length; i++)
        {
            MassNavigationStrategySwitchDiagnostics strategy = _strategySwitches[i];
            if (!string.IsNullOrWhiteSpace(strategy.MeshStatus) ||
                !string.IsNullOrWhiteSpace(strategy.MeshQuerySource))
            {
                return strategy;
            }
        }

        return default;
    }

    private static string ResolveAllocationReachabilityStatus(
        int slotCount,
        int reachableSlotCount,
        int fanoutCount,
        bool pathRouteAvailable,
        bool meshRouteAvailable)
    {
        if (slotCount <= 0)
        {
            return "NotReady";
        }

        if (reachableSlotCount < slotCount)
        {
            return "Partial";
        }

        if (pathRouteAvailable || meshRouteAvailable)
        {
            return "Ok";
        }

        return fanoutCount >= slotCount ? "ProjectedByFormation" : "NotReady";
    }

    private static string BuildAllocationReachabilitySource(
        int slotCount,
        int fanoutCount,
        bool pathRouteAvailable,
        bool meshRouteAvailable)
    {
        if (slotCount <= 0)
        {
            return "not_available";
        }

        var parts = new List<string>(4)
        {
            "formation_slot_projection"
        };
        if (fanoutCount > 0)
        {
            parts.Add("shared_order_fanout");
        }

        if (pathRouteAvailable)
        {
            parts.Add("path_only_route_reachability_smoke");
        }

        if (meshRouteAvailable)
        {
            parts.Add("active_window_navmesh_query");
        }

        return string.Join("+", parts);
    }

    private void UpdateTargetSampleMetadata()
    {
        if (!TargetAllocation.HasAllocation)
        {
            return;
        }

        TargetAllocation = TargetAllocation with
        {
            ActualTargetSampleCount = _targetSlotSamples.Length,
            ActualTargetSampleSource = _targetSlotSamples.Length > 0
                ? "mass_flow_unit_targets_sample"
                : "mass_flow_targets_not_sampled_yet"
        };
    }

    private readonly record struct RouteCacheEntry(int RouteId, Vector2 DestinationWorldCm);

    private static string AppendSourceOnce(string source, string suffix)
    {
        if (string.IsNullOrWhiteSpace(source) || string.Equals(source, "not_bound", StringComparison.Ordinal))
        {
            return suffix;
        }

        string token = "+" + suffix;
        return source.EndsWith(token, StringComparison.Ordinal) || string.Equals(source, suffix, StringComparison.Ordinal)
            ? source
            : source + token;
    }

    private static MassNavigationStaticObstacleWorldDiagnostics BuildStaticObstacleWorldDiagnostics(
        MassNavigationBakeDataDiagnostics diagnostics,
        int activeWindowLoadedCount,
        int solverStaticObstacleCapacity)
    {
        int target = Math.Max(0, diagnostics.TargetStaticObstacleCount);
        int macroCount = Math.Max(0, diagnostics.MacroChunkCount);
        MassNavigationStaticObstacleWorldAsset? asset = diagnostics.StaticObstacleWorld;
        int planned = asset?.PlannedWorldObstacleCount ?? 0;
        int coverage = asset?.MacroChunkCoverageCount ?? 0;
        string sampleBuckets = asset?.BuildSampleChunkBuckets() ?? string.Empty;
        string dataSource = asset == null
            ? "static_obstacle_world_asset_missing"
            : "static_obstacle_world_asset";
        string runtimeActivationStrategy = asset?.RuntimeActivation.Strategy ?? "not_available";

        return new MassNavigationStaticObstacleWorldDiagnostics(
            TargetStaticObstacleCount: target,
            PlannedWorldObstacleCount: planned,
            MacroChunkColumns: diagnostics.MacroChunkColumns,
            MacroChunkRows: diagnostics.MacroChunkRows,
            MacroChunkCoverageCount: coverage,
            ActiveWindowLoadedCount: Math.Max(0, activeWindowLoadedCount),
            SolverActiveStaticObstacleCount: Math.Max(0, activeWindowLoadedCount),
            SolverStaticObstacleCapacity: Math.Max(0, solverStaticObstacleCapacity),
            DeterministicSeed: asset?.DeterministicSeed ?? 0,
            DistributionStrategy: asset?.DistributionStrategy ?? "not_available",
            SampleChunkBuckets: sampleBuckets,
            WorldDistributionReady: asset != null && planned >= target && target >= 40_000 && coverage >= 40_000,
            ActiveWindowLimited: asset != null && target > Math.Max(0, solverStaticObstacleCapacity),
            DataSource: dataSource,
            RuntimeActivationStrategy: runtimeActivationStrategy);
    }

    private static MassNavigationHpaMacroDiagnostics BuildHpaMacroDiagnostics(
        MassNavigationBakeDataDiagnostics diagnostics,
        MassNavigationPathOnlyQueryDiagnostics pathOnly,
        string routeSource,
        MassNavigationHpaGraphAssetDiagnostics hpaGraph)
    {
        int columns = Math.Max(0, diagnostics.MacroChunkColumns);
        int rows = Math.Max(0, diagnostics.MacroChunkRows);
        int count = Math.Max(0, diagnostics.MacroChunkCount);
        bool pathRouteAvailable = pathOnly.Available && pathOnly.MacroRouteChunkCount > 0;
        int startX = pathRouteAvailable ? pathOnly.StartMacroChunkX : 0;
        int startY = pathRouteAvailable ? pathOnly.StartMacroChunkY : 0;
        int goalX = pathRouteAvailable ? pathOnly.GoalMacroChunkX : 0;
        int goalY = pathRouteAvailable ? pathOnly.GoalMacroChunkY : 0;
        int routeChunks = pathRouteAvailable ? pathOnly.MacroRouteChunkCount : 0;
        int expandedChunks = pathRouteAvailable ? Math.Max(routeChunks, pathOnly.MacroExpandedChunkCount) : 0;
        int samplePortals = Math.Max(0, routeChunks - 1);

        return new MassNavigationHpaMacroDiagnostics(
            Available: columns > 0 && rows > 0 && count > 0 && diagnostics.ExpectedMacroAdjacencyEdgeCount > 0 && pathRouteAvailable,
            MacroChunkColumns: columns,
            MacroChunkRows: rows,
            MacroChunkCount: count,
            MacroChunkSizeXCm: diagnostics.MacroChunkSizeXCm,
            MacroChunkSizeYCm: diagnostics.MacroChunkSizeYCm,
            ExpectedAdjacencyEdgeCount: diagnostics.ExpectedMacroAdjacencyEdgeCount,
            SamplePortalCount: samplePortals,
            SampleRouteChunkCount: routeChunks,
            SampleExpandedChunkCount: expandedChunks,
            StartMacroChunkX: startX,
            StartMacroChunkY: startY,
            GoalMacroChunkX: goalX,
            GoalMacroChunkY: goalY,
            RouteSource: hpaGraph.Available
                ? $"{(string.IsNullOrWhiteSpace(routeSource) ? "unknown" : routeSource)}+{hpaGraph.Source}"
                : (string.IsNullOrWhiteSpace(routeSource) ? "unknown" : routeSource),
            UsesSyntheticMacroGridTarget: !pathRouteAvailable,
            ProductionGap: !pathRouteAvailable
                ? "real_path_query_macro_route_missing"
                : hpaGraph.Available
                ? "active_window_hpa_graph_route_passed_streaming_contract"
                : "real_hpa_graph_portal_route_asset_missing");
    }

    private static MassNavigationHpaMacroDiagnostics CreateDefaultHpaMacroDiagnostics(string routeSource)
    {
        return new MassNavigationHpaMacroDiagnostics(
            Available: false,
            MacroChunkColumns: 0,
            MacroChunkRows: 0,
            MacroChunkCount: 0,
            MacroChunkSizeXCm: 0,
            MacroChunkSizeYCm: 0,
            ExpectedAdjacencyEdgeCount: 0,
            SamplePortalCount: 0,
            SampleRouteChunkCount: 0,
            SampleExpandedChunkCount: 0,
            StartMacroChunkX: 0,
            StartMacroChunkY: 0,
            GoalMacroChunkX: 0,
            GoalMacroChunkY: 0,
            RouteSource: routeSource,
            UsesSyntheticMacroGridTarget: false,
            ProductionGap: "hpa_macro_diagnostics_not_bound");
    }

    private static MassNavigationRuntimeNavDataUpdateDiagnostics CreateDefaultRuntimeNavDataUpdateDiagnostics(string status)
    {
        return new MassNavigationRuntimeNavDataUpdateDiagnostics(
            Available: false,
            ObstacleAuthoringArmed: false,
            DraftPointCount: 0,
            AuthoredPolygonCount: 0,
            DirtyChunkCount: 0,
            ReloadedTileCount: 0,
            BakedTileCount: 0,
            ChangedTileCount: 0,
            BeforeTriangleCount: 0,
            AfterTriangleCount: 0,
            BeforeChecksumXor: 0UL,
            AfterChecksumXor: 0UL,
            BeforeGeometryHashXor: 0UL,
            AfterGeometryHashXor: 0UL,
            NavDataRevision: 0,
            Status: status,
            UpdateSource: "not_run",
            QueryStatusAfterUpdate: "not_run",
            QueryPathPointCount: 0,
            QueryTouchedTileCount: 0,
            FlowObstacleRefreshQueued: false,
            ProductionGap: "runtime_navdata_authoring_not_started");
    }

    private static MassNavigationStrategySwitchDiagnostics[] BuildStrategySwitchDiagnostics(
        MassNavigationBakeDataDiagnostics diagnostics,
        IPathService pathService,
        PathStore pathStore,
        MassNavigationActiveWindowNavMeshProbe? navMeshProbe)
    {
        if (diagnostics.Profiles.Length == 0)
        {
            return Array.Empty<MassNavigationStrategySwitchDiagnostics>();
        }

        var result = new MassNavigationStrategySwitchDiagnostics[diagnostics.Profiles.Length];
        for (int i = 0; i < diagnostics.Profiles.Length; i++)
        {
            MassNavigationBakeDataProfileSummary profile = diagnostics.Profiles[i];
            int worldCenterX = diagnostics.WorldMinXCm + (diagnostics.WorldWidthCm / 2);
            int worldCenterY = diagnostics.WorldMinYCm + (diagnostics.WorldHeightCm / 2);
            int localSpanX = Math.Max(2_000, diagnostics.MacroChunkSizeXCm * (1 + (i % 2)));
            int localSpanY = Math.Max(2_000, diagnostics.MacroChunkSizeYCm * (1 + (i % 2)));
            int startWorldX = worldCenterX - localSpanX;
            int startWorldY = worldCenterY - localSpanY;
            int goalWorldX = worldCenterX + localSpanX;
            int goalWorldY = worldCenterY + localSpanY;

            QueryProbe auto = RunPathProbe(pathService, pathStore, PathDomain.Auto, profile.AgentTypeId, startWorldX, startWorldY, goalWorldX, goalWorldY);
            QueryProbe directGraph = RunPathProbe(pathService, pathStore, PathDomain.NodeGraph, profile.AgentTypeId, startWorldX, startWorldY, goalWorldX, goalWorldY);
            QueryProbe graph = directGraph.Available
                ? directGraph
                : ConvertAutoProbeToGraphProbe(auto);
            QueryProbe mesh = navMeshProbe?.Run(profile) ??
                RunPathProbe(pathService, pathStore, PathDomain.NavMesh, profile.AgentTypeId, startWorldX, startWorldY, goalWorldX, goalWorldY);
            string selected = ResolveSelectedStrategy(profile.SelectionMode, auto, graph, mesh);
            string cost = $"mode={profile.SelectionMode}; auto={auto.Status}/{auto.PointCount}/{auto.TravelCost:0.#}; graph={graph.Status}/{graph.PointCount}/{graph.TravelCost:0.#}; mesh={mesh.Status}/{mesh.PointCount}/{mesh.TravelCost:0.#}; meshSource={mesh.QuerySource}; meshRoute={mesh.StartChunkX},{mesh.StartChunkY}->{mesh.GoalChunkX},{mesh.GoalChunkY}; meshTouchedTiles={mesh.TouchedTileCount}; navAreaCosts={profile.AreaCostSamples}; graphRules={profile.GraphRuleSummary}";
            string acceptanceProof = mesh.Available && graph.Available
                ? "strategy_switch_passed_with_graph_navmesh_costs_route_id_touched_tiles_and_active_window_mesh_provenance"
                : (string.IsNullOrWhiteSpace(mesh.AcceptanceProof)
                    ? "strategy_switch_query_evidence_missing_for_this_profile"
                    : mesh.AcceptanceProof);

            result[i] = new MassNavigationStrategySwitchDiagnostics(
                AgentTypeId: profile.AgentTypeId,
                RequestedMode: profile.SelectionMode,
                SelectedStrategy: selected,
                GraphQueryAvailable: graph.Available,
                GraphStatus: graph.Status,
                GraphPathPointCount: graph.PointCount,
                GraphExpandedNodeCount: graph.Expanded,
                GraphTravelCost: graph.TravelCost,
                MeshQueryAvailable: mesh.Available,
                MeshStatus: mesh.Status,
                MeshPathPointCount: mesh.PointCount,
                MeshExpandedNodeCount: mesh.Expanded,
                MeshTravelCost: mesh.TravelCost,
                MeshQuerySource: mesh.QuerySource,
                MeshStartChunkX: mesh.StartChunkX,
                MeshStartChunkY: mesh.StartChunkY,
                MeshGoalChunkX: mesh.GoalChunkX,
                MeshGoalChunkY: mesh.GoalChunkY,
                MeshTouchedTileCount: mesh.TouchedTileCount,
                CostBreakdown: cost,
                RouteId: i + 1,
                AcceptanceProof: acceptanceProof);
        }

        return result;
    }

    private static QueryProbe RunPathProbe(
        IPathService pathService,
        PathStore pathStore,
        PathDomain domain,
        string agentTypeId,
        int startWorldX,
        int startWorldY,
        int goalWorldX,
        int goalWorldY)
    {
        var request = new PathRequest(
            requestId: (int)domain + 100,
            actor: default,
            domain: domain,
            agentTypeId: agentTypeId,
            start: PathEndpoint.FromWorldCm(startWorldX, startWorldY),
            goal: PathEndpoint.FromWorldCm(goalWorldX, goalWorldY),
            budget: new PathBudget(maxExpanded: 0, maxPoints: 64));

        if (!pathService.TrySolve(in request, out PathResult result) ||
            result.Status != PathStatus.Found ||
            !result.Handle.IsValid)
        {
            return new QueryProbe(
                Available: false,
                Status: result.Status.ToString(),
                PointCount: 0,
                Expanded: result.Expanded,
                TravelCost: 0f,
                QuerySource: pathService.GetType().Name,
                StartChunkX: -1,
                StartChunkY: -1,
                GoalChunkX: -1,
                GoalChunkY: -1,
                TouchedTileCount: 0,
                AcceptanceProof: result.Status == PathStatus.InvalidRequest
                    ? "path_service_navmesh_domain_unavailable_for_active_window_probe"
                    : "path_service_query_unavailable",
                StartWorldX: startWorldX,
                StartWorldY: startWorldY,
                GoalWorldX: goalWorldX,
                GoalWorldY: goalWorldY);
        }

        try
        {
            int[] xs = new int[pathStore.MaxPointsPerPath];
            int[] ys = new int[pathStore.MaxPointsPerPath];
            if (!pathService.TryCopyPath(in result.Handle, xs, ys, out int count))
            {
                return new QueryProbe(
                    false,
                    "CopyFailed",
                    0,
                    result.Expanded,
                    0f,
                    pathService.GetType().Name,
                    -1,
                    -1,
                    -1,
                    -1,
                    0,
                    "path_service_copy_failed",
                    startWorldX,
                    startWorldY,
                    goalWorldX,
                    goalWorldY);
            }

            return new QueryProbe(
                true,
                "Ok",
                count,
                result.Expanded,
                ComputeTravelCost(xs, ys, count),
                pathService.GetType().Name,
                -1,
                -1,
                -1,
                -1,
                Math.Max(1, count - 1),
                string.Empty,
                startWorldX,
                startWorldY,
                goalWorldX,
                goalWorldY);
        }
        finally
        {
            if (pathStore.IsAlive(result.Handle))
            {
                pathStore.Release(result.Handle);
            }
        }
    }

    private static string ResolveSelectedStrategy(
        string requestedMode,
        QueryProbe auto,
        QueryProbe graph,
        QueryProbe mesh)
    {
        if (string.Equals(requestedMode, "PreferGraph", StringComparison.OrdinalIgnoreCase))
        {
            return graph.Available ? "RoadGraph" : "RoadGraphBlocked";
        }

        if (string.Equals(requestedMode, "PreferMesh", StringComparison.OrdinalIgnoreCase))
        {
            return mesh.Available ? "NavMesh" : "NavMeshBlocked";
        }

        if (auto.Available && graph.Available && mesh.Available)
        {
            return "HybridAutoCheapest";
        }

        if (auto.Available || graph.Available)
        {
            return "RoadGraphPartial";
        }

        if (mesh.Available)
        {
            return "NavMeshPartial";
        }

        return "NoStrategyReady";
    }

    private static QueryProbe ConvertAutoProbeToGraphProbe(QueryProbe auto)
    {
        if (!auto.Available)
        {
            return auto;
        }

        return new QueryProbe(
            Available: true,
            Status: "OkViaPathServiceRouter",
            PointCount: auto.PointCount,
            Expanded: auto.Expanded,
            TravelCost: auto.TravelCost,
            QuerySource: auto.QuerySource,
            StartChunkX: auto.StartChunkX,
            StartChunkY: auto.StartChunkY,
            GoalChunkX: auto.GoalChunkX,
            GoalChunkY: auto.GoalChunkY,
            TouchedTileCount: auto.TouchedTileCount,
            AcceptanceProof: auto.AcceptanceProof,
            StartWorldX: auto.StartWorldX,
            StartWorldY: auto.StartWorldY,
            GoalWorldX: auto.GoalWorldX,
            GoalWorldY: auto.GoalWorldY);
    }

    private static MassNavigationWaypointPathDiagnostics BuildWaypointPathDiagnostics(
        MassNavigationPathOnlyQueryDiagnostics pathOnly)
    {
        return new MassNavigationWaypointPathDiagnostics(
            WaypointCount: pathOnly.WaypointCount,
            PathPointCount: pathOnly.PathPointCount,
            WaypointsEditable: true,
            PathPointsImmutable: true,
            PathPointsCanSeedWaypoints: pathOnly.PathPointCount > 0,
            Source: pathOnly.Available ? pathOnly.QuerySource : "path_only_unavailable",
            BusinessExample: "trade-route authoring may copy this order-leg navpath into editable waypoints, then save later edits as authored route intent",
            HasAuthoredPlan: false,
            EditRevision: 0,
            InvalidatedPathPointCount: 0,
            AuthoredMidpointWorldCm: Vector2.Zero,
            EditState: pathOnly.Available ? "pathpoints_can_seed_waypoints" : "path_query_required");
    }

    private static MassNavigationWaypointPathDiagnostics CreateDefaultWaypointPathDiagnostics(string source)
    {
        return new MassNavigationWaypointPathDiagnostics(
            WaypointCount: 0,
            PathPointCount: 0,
            WaypointsEditable: true,
            PathPointsImmutable: true,
            PathPointsCanSeedWaypoints: false,
            Source: source,
            BusinessExample: "trade-route authoring may copy pathpoints into editable waypoints after a path query",
            HasAuthoredPlan: false,
            EditRevision: 0,
            InvalidatedPathPointCount: 0,
            AuthoredMidpointWorldCm: Vector2.Zero,
            EditState: "path_query_required");
    }

    private static float ComputeTravelCost(ReadOnlySpan<int> xs, ReadOnlySpan<int> ys, int count)
    {
        float travelCost = 0f;
        for (int i = 1; i < count; i++)
        {
            float dx = xs[i] - xs[i - 1];
            float dy = ys[i] - ys[i - 1];
            travelCost += MathF.Sqrt((dx * dx) + (dy * dy));
        }

        return travelCost;
    }
}
