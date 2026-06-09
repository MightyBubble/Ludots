using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.Core.Modding;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Diagnostics;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Spatial;

namespace MassNavigationMod.Runtime;

public enum MassNavigationBakeDataDomain : byte
{
    NavMesh = 0,
    RoadGraph = 1,
    FlowField = 2,
    StaticObstacle = 3,
}

public readonly record struct MassNavigationBakeDataDomainSummary(
    MassNavigationBakeDataDomain Domain,
    int TotalChunks,
    int BakedChunks,
    int MissingChunks,
    int DirtyChunks,
    int FailedChunks,
    int NotLoadedChunks,
    int CoveragePercent)
{
    public bool IsComplete => BakedChunks == TotalChunks &&
        MissingChunks == 0 &&
        DirtyChunks == 0 &&
        FailedChunks == 0 &&
        NotLoadedChunks == 0;
}

public readonly record struct MassNavigationBakeDataProfileSummary(
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

public sealed class MassNavigationBakeDataDiagnostics
{
    public const string SchemaVersion = "mass-navigation.bake-data-diagnostics.v1";

    private MassNavigationBakeDataDiagnostics(
        string mapId,
        string logicHeightmapSource,
        int worldMinXCm,
        int worldMinYCm,
        int worldWidthCm,
        int worldHeightCm,
        int macroChunkColumns,
        int macroChunkRows,
        int macroChunkSizeXCm,
        int macroChunkSizeYCm,
        int activeNavMeshMinChunkX,
        int activeNavMeshMinChunkY,
        int activeNavMeshMaxChunkX,
        int activeNavMeshMaxChunkY,
        MassNavigationBakeDataDomainSummary navMesh,
        MassNavigationBakeDataDomainSummary roadGraph,
        MassNavigationBakeDataDomainSummary flowField,
        MassNavigationBakeDataDomainSummary staticObstacle,
        MassNavigationBakeDataProfileSummary[] profiles,
        int navMeshLayerCount,
        int navMeshProfileCount,
        int navMeshAreaCostCount,
        int navMeshForbiddenAreaCount,
        int authoredStaticObstacleCount,
        int targetStaticObstacleCount,
        MassNavigationStaticObstacleWorldAsset? staticObstacleWorld,
        int expectedMacroAdjacencyEdgeCount,
        bool hpaOverlayRequired,
        bool pathInspectorRequired,
        bool bakeOverlayRequired)
    {
        MapId = mapId;
        LogicHeightmapSource = logicHeightmapSource ?? string.Empty;
        WorldMinXCm = worldMinXCm;
        WorldMinYCm = worldMinYCm;
        WorldWidthCm = worldWidthCm;
        WorldHeightCm = worldHeightCm;
        MacroChunkColumns = macroChunkColumns;
        MacroChunkRows = macroChunkRows;
        MacroChunkSizeXCm = macroChunkSizeXCm;
        MacroChunkSizeYCm = macroChunkSizeYCm;
        ActiveNavMeshMinChunkX = activeNavMeshMinChunkX;
        ActiveNavMeshMinChunkY = activeNavMeshMinChunkY;
        ActiveNavMeshMaxChunkX = activeNavMeshMaxChunkX;
        ActiveNavMeshMaxChunkY = activeNavMeshMaxChunkY;
        NavMesh = navMesh;
        RoadGraph = roadGraph;
        FlowField = flowField;
        StaticObstacle = staticObstacle;
        Profiles = profiles;
        NavMeshLayerCount = navMeshLayerCount;
        NavMeshProfileCount = navMeshProfileCount;
        NavMeshAreaCostCount = navMeshAreaCostCount;
        NavMeshForbiddenAreaCount = navMeshForbiddenAreaCount;
        AuthoredStaticObstacleCount = authoredStaticObstacleCount;
        TargetStaticObstacleCount = targetStaticObstacleCount;
        StaticObstacleWorld = staticObstacleWorld;
        ExpectedMacroAdjacencyEdgeCount = expectedMacroAdjacencyEdgeCount;
        HpaOverlayRequired = hpaOverlayRequired;
        PathInspectorRequired = pathInspectorRequired;
        BakeOverlayRequired = bakeOverlayRequired;
    }

    public string MapId { get; }
    public string LogicHeightmapSource { get; }
    public int WorldMinXCm { get; }
    public int WorldMinYCm { get; }
    public int WorldWidthCm { get; }
    public int WorldHeightCm { get; }
    public int MacroChunkColumns { get; }
    public int MacroChunkRows { get; }
    public int MacroChunkCount => MacroChunkColumns * MacroChunkRows;
    public int MacroChunkSizeXCm { get; }
    public int MacroChunkSizeYCm { get; }
    public int ActiveNavMeshMinChunkX { get; }
    public int ActiveNavMeshMinChunkY { get; }
    public int ActiveNavMeshMaxChunkX { get; }
    public int ActiveNavMeshMaxChunkY { get; }
    public bool HasActiveNavMeshWindow =>
        ActiveNavMeshMinChunkX >= 0 &&
        ActiveNavMeshMinChunkY >= 0 &&
        ActiveNavMeshMaxChunkX >= ActiveNavMeshMinChunkX &&
        ActiveNavMeshMaxChunkY >= ActiveNavMeshMinChunkY;
    public MassNavigationBakeDataDomainSummary NavMesh { get; }
    public MassNavigationBakeDataDomainSummary RoadGraph { get; }
    public MassNavigationBakeDataDomainSummary FlowField { get; }
    public MassNavigationBakeDataDomainSummary StaticObstacle { get; }
    public MassNavigationBakeDataProfileSummary[] Profiles { get; }
    public int NavMeshLayerCount { get; }
    public int NavMeshProfileCount { get; }
    public int NavMeshAreaCostCount { get; }
    public int NavMeshForbiddenAreaCount { get; }
    public int AuthoredStaticObstacleCount { get; }
    public int TargetStaticObstacleCount { get; }
    public MassNavigationStaticObstacleWorldAsset? StaticObstacleWorld { get; }
    public int ExpectedMacroAdjacencyEdgeCount { get; }
    public bool HpaOverlayRequired { get; }
    public bool PathInspectorRequired { get; }
    public bool BakeOverlayRequired { get; }

    public static MassNavigationBakeDataDiagnostics Create(
        string mapId,
        WorldSizeSpec worldSize,
        MassNavigationBakeDataConfig config,
        MassNavigationWorldConfig worldConfig,
        NavMeshBakeConfig? navMeshConfig,
        PathingConfig? pathingConfig,
        NavBakeDiagnosticsDocument? navBakeDiagnostics = null,
        IVirtualFileSystem? vfs = null,
        IEnumerable<string>? loadedModIds = null)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            throw new ArgumentException("mapId is required.", nameof(mapId));
        }

        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (worldConfig == null)
        {
            throw new ArgumentNullException(nameof(worldConfig));
        }

        config.Validate();

        WorldAabbCm bounds = worldSize.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("Mass-nav bake/data diagnostics requires positive board world bounds.");
        }

        int macroColumns = config.MacroChunkColumns;
        int macroRows = config.MacroChunkRows;
        int macroChunkSizeX = ResolveMacroChunkSize(bounds.Width, macroColumns, "width");
        int macroChunkSizeY = ResolveMacroChunkSize(bounds.Height, macroRows, "height");
        int macroChunkCount = checked(macroColumns * macroRows);
        int expectedPortalEdges = checked(((macroColumns - 1) * macroRows) + ((macroRows - 1) * macroColumns));
        int navLayerCount = navMeshConfig?.Layers?.Count ?? 0;
        int navProfileCount = navMeshConfig?.Profiles?.Count ?? 0;
        int navAreaCount = navMeshConfig?.Areas?.Count ?? 0;
        int navForbiddenAreaCount = CountForbiddenAreas(pathingConfig);
        ValidatePathingReferences(navMeshConfig, pathingConfig);
        MassNavigationBakeDataProfileSummary[] profiles = BuildProfileSummaries(pathingConfig);
        MassNavigationBakeDataDomainSummary navMeshSummary = CreateNavMeshSummary(macroChunkCount, navBakeDiagnostics);
        MassNavigationStaticObstacleWorldAsset? staticObstacleWorld = TryLoadStaticObstacleWorld(
            vfs,
            loadedModIds,
            mapId,
            macroColumns,
            macroRows,
            config.TargetStaticObstacleCount);
        MassNavigationBakeDataDomainSummary staticObstacleSummary = CreateStaticObstacleSummary(
            macroChunkCount,
            staticObstacleWorld);
        int authoredStaticObstacles = staticObstacleWorld?.PlannedWorldObstacleCount ?? worldConfig.Obstacles.Length;
        ResolveActiveNavMeshWindow(
            navBakeDiagnostics,
            out int activeNavMeshMinChunkX,
            out int activeNavMeshMinChunkY,
            out int activeNavMeshMaxChunkX,
            out int activeNavMeshMaxChunkY);

        return new MassNavigationBakeDataDiagnostics(
            mapId,
            navBakeDiagnostics?.SourceMapPath ?? string.Empty,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            macroColumns,
            macroRows,
            macroChunkSizeX,
            macroChunkSizeY,
            activeNavMeshMinChunkX,
            activeNavMeshMinChunkY,
            activeNavMeshMaxChunkX,
            activeNavMeshMaxChunkY,
            navMeshSummary,
            CreateNotLoadedSummary(MassNavigationBakeDataDomain.RoadGraph, macroChunkCount),
            CreateNotLoadedSummary(MassNavigationBakeDataDomain.FlowField, macroChunkCount),
            staticObstacleSummary,
            profiles,
            navLayerCount,
            navProfileCount,
            navAreaCount,
            navForbiddenAreaCount,
            authoredStaticObstacles,
            config.TargetStaticObstacleCount,
            staticObstacleWorld,
            expectedPortalEdges,
            config.HpaOverlayRequired,
            config.PathInspectorRequired,
            config.BakeOverlayRequired);
    }

    private static void ResolveActiveNavMeshWindow(
        NavBakeDiagnosticsDocument? document,
        out int minChunkX,
        out int minChunkY,
        out int maxChunkX,
        out int maxChunkY)
    {
        if (document == null ||
            document.ActiveWindowMinChunkX < 0 ||
            document.ActiveWindowMinChunkY < 0 ||
            document.ActiveWindowMaxChunkX < document.ActiveWindowMinChunkX ||
            document.ActiveWindowMaxChunkY < document.ActiveWindowMinChunkY)
        {
            minChunkX = -1;
            minChunkY = -1;
            maxChunkX = -1;
            maxChunkY = -1;
            return;
        }

        minChunkX = document.ActiveWindowMinChunkX;
        minChunkY = document.ActiveWindowMinChunkY;
        maxChunkX = document.ActiveWindowMaxChunkX;
        maxChunkY = document.ActiveWindowMaxChunkY;
    }

    private static MassNavigationStaticObstacleWorldAsset? TryLoadStaticObstacleWorld(
        IVirtualFileSystem? vfs,
        IEnumerable<string>? loadedModIds,
        string mapId,
        int macroColumns,
        int macroRows,
        int targetStaticObstacleCount)
    {
        if (vfs == null)
        {
            return null;
        }

        MassNavigationStaticObstacleWorldAsset? asset = MassNavigationStaticObstacleWorldAssetLoader.TryLoad(
            vfs,
            loadedModIds,
            mapId);
        asset?.Validate(mapId, macroColumns, macroRows, targetStaticObstacleCount);
        return asset;
    }

    private static MassNavigationBakeDataDomainSummary CreateNavMeshSummary(
        int macroChunkCount,
        NavBakeDiagnosticsDocument? document)
    {
        if (document == null)
        {
            return CreateNotLoadedSummary(MassNavigationBakeDataDomain.NavMesh, macroChunkCount);
        }

        int documentTargetChunks = document.TargetChunkCount;
        int documentWorldChunks = document.WorldChunkCount > 0
            ? document.WorldChunkCount
            : documentTargetChunks;
        if (documentTargetChunks <= 0)
        {
            throw new InvalidOperationException("Mass-nav navmesh diagnostics target chunk count must be positive.");
        }

        if (documentTargetChunks > macroChunkCount)
        {
            throw new InvalidOperationException(
                $"Mass-nav navmesh diagnostics target chunk count cannot exceed macro chunk contract. diagnostics={documentTargetChunks}, macroChunks={macroChunkCount}.");
        }

        if (documentWorldChunks != macroChunkCount)
        {
            throw new InvalidOperationException(
                $"Mass-nav navmesh diagnostics world chunk count must match macro chunk contract. diagnosticsWorld={documentWorldChunks}, macroChunks={macroChunkCount}.");
        }

        int baked = 0;
        int failed = 0;
        int missing = 0;
        int dirty = 0;
        int notLoaded = 0;

        for (int i = 0; i < document.LayerProfiles.Count; i++)
        {
            NavBakeLayerProfileSummary profile = document.LayerProfiles[i];
            if (profile.TargetChunks != documentTargetChunks)
            {
                throw new InvalidOperationException(
                    $"Mass-nav navmesh diagnostics layer/profile '{profile.LayerId}/{profile.ProfileId}' targetChunks must match document target chunk count. targetChunks={profile.TargetChunks}, documentTargetChunks={documentTargetChunks}.");
            }

            baked += profile.BakedTiles;
            failed += profile.FailedTiles;
            missing += profile.MissingTiles;
            dirty += profile.DirtyTiles;
            notLoaded += profile.NotLoadedTiles + Math.Max(0, macroChunkCount - documentTargetChunks);
        }

        int observedTotal = document.LayerProfiles.Count * macroChunkCount;
        int coverage = observedTotal > 0
            ? (int)MathF.Round(baked * 100f / observedTotal)
            : 0;

        return new MassNavigationBakeDataDomainSummary(
            MassNavigationBakeDataDomain.NavMesh,
            observedTotal,
            baked,
            missing,
            dirty,
            failed,
            notLoaded,
            coverage);
    }

    private static MassNavigationBakeDataDomainSummary CreateStaticObstacleSummary(
        int macroChunkCount,
        MassNavigationStaticObstacleWorldAsset? asset)
    {
        if (asset == null)
        {
            return CreateNotLoadedSummary(MassNavigationBakeDataDomain.StaticObstacle, macroChunkCount);
        }

        int baked = Math.Max(0, asset.MacroChunkCoverageCount);
        int notLoaded = Math.Max(0, macroChunkCount - baked);
        int coverage = macroChunkCount > 0
            ? (int)MathF.Round(baked * 100f / macroChunkCount)
            : 0;

        return new MassNavigationBakeDataDomainSummary(
            MassNavigationBakeDataDomain.StaticObstacle,
            macroChunkCount,
            baked,
            MissingChunks: 0,
            DirtyChunks: 0,
            FailedChunks: 0,
            NotLoadedChunks: notLoaded,
            CoveragePercent: coverage);
    }

    private static int ResolveMacroChunkSize(int worldExtentCm, int chunkCount, string axisName)
    {
        if (worldExtentCm % chunkCount != 0)
        {
            throw new InvalidOperationException(
                $"Mass-nav bake/data macro chunk {axisName} must divide board world extent exactly. extent={worldExtentCm}, chunks={chunkCount}.");
        }

        return worldExtentCm / chunkCount;
    }

    private static MassNavigationBakeDataDomainSummary CreateNotLoadedSummary(MassNavigationBakeDataDomain domain, int totalChunks)
    {
        return new MassNavigationBakeDataDomainSummary(
            domain,
            totalChunks,
            BakedChunks: 0,
            MissingChunks: 0,
            DirtyChunks: 0,
            FailedChunks: 0,
            NotLoadedChunks: totalChunks,
            CoveragePercent: 0);
    }

    private static MassNavigationBakeDataProfileSummary[] BuildProfileSummaries(PathingConfig? pathingConfig)
    {
        if (pathingConfig?.AgentTypes == null || pathingConfig.AgentTypes.Count == 0)
        {
            return Array.Empty<MassNavigationBakeDataProfileSummary>();
        }

        var result = new List<MassNavigationBakeDataProfileSummary>(pathingConfig.AgentTypes.Count);
        for (int i = 0; i < pathingConfig.AgentTypes.Count; i++)
        {
            PathingAgentTypeConfig? agent = pathingConfig.AgentTypes[i];
            if (agent == null)
            {
                continue;
            }

            result.Add(new MassNavigationBakeDataProfileSummary(
                agent.Id ?? string.Empty,
                agent.ProfileId ?? string.Empty,
                agent.Layer,
                agent.Selection?.Mode.ToString() ?? string.Empty,
                agent.NavMesh?.AreaCosts?.Count ?? 0,
                agent.NodeGraph?.TagCostRules?.Count ?? 0,
                agent.NodeGraph?.ForbiddenTagsAny?.Count ?? 0,
                ComputeRepresentativeAreaCost(agent.NavMesh),
                FormatAreaCostSamples(agent.NavMesh),
                FormatGraphRuleSummary(agent.NodeGraph),
                FormatTagList(agent.NodeGraph?.RequiredTagsAll),
                FormatTagList(agent.NodeGraph?.ForbiddenTagsAny)));
        }

        return result.ToArray();
    }

    private static string FormatAreaCostSamples(PathingNavMeshConfig? navMesh)
    {
        if (navMesh?.AreaCosts == null || navMesh.AreaCosts.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(",",
            navMesh.AreaCosts
                .Where(cost => cost != null)
                .OrderBy(cost => cost.AreaId)
                .Select(cost => $"{cost.AreaId}:{cost.Cost:0.###}"));
    }

    private static string FormatGraphRuleSummary(PathingNodeGraphConfig? nodeGraph)
    {
        if (nodeGraph?.TagCostRules == null || nodeGraph.TagCostRules.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(",",
            nodeGraph.TagCostRules
                .Where(rule => rule != null)
                .Select(rule => $"{rule.Tag}:mul={rule.CostMul:0.###},add={rule.CostAdd:0.###},block={rule.Block}"));
    }

    private static string FormatTagList(IReadOnlyCollection<string>? tags)
    {
        if (tags == null || tags.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(",", tags.Where(tag => !string.IsNullOrWhiteSpace(tag)));
    }

    private static int CountForbiddenAreas(PathingConfig? pathingConfig)
    {
        if (pathingConfig?.AgentTypes == null || pathingConfig.AgentTypes.Count == 0)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < pathingConfig.AgentTypes.Count; i++)
        {
            PathingAgentTypeConfig? agent = pathingConfig.AgentTypes[i];
            if (agent?.NodeGraph?.ForbiddenTagsAny != null)
            {
                count += agent.NodeGraph.ForbiddenTagsAny.Count;
            }
        }

        return count;
    }

    private static float ComputeRepresentativeAreaCost(PathingNavMeshConfig? navMesh)
    {
        if (navMesh?.AreaCosts == null || navMesh.AreaCosts.Count == 0)
        {
            return 1f;
        }

        float minPositive = float.MaxValue;
        float sum = 0f;
        int count = 0;
        for (int i = 0; i < navMesh.AreaCosts.Count; i++)
        {
            PathingAreaCostConfig? cost = navMesh.AreaCosts[i];
            if (cost == null || float.IsNaN(cost.Cost) || cost.Cost <= 0f)
            {
                continue;
            }

            minPositive = Math.Min(minPositive, cost.Cost);
            sum += cost.Cost;
            count++;
        }

        if (count <= 0)
        {
            return 1f;
        }

        float average = sum / count;
        return Math.Max(0.001f, Math.Min(minPositive, average));
    }

    private static void ValidatePathingReferences(NavMeshBakeConfig? navMeshConfig, PathingConfig? pathingConfig)
    {
        if (pathingConfig?.AgentTypes == null || pathingConfig.AgentTypes.Count == 0)
        {
            return;
        }

        if (navMeshConfig?.Profiles == null || navMeshConfig.Layers == null || navMeshConfig.Areas == null)
        {
            throw new InvalidOperationException("Mass-nav bake/data diagnostics requires navmesh profiles, layers, and areas before validating pathing agent types.");
        }

        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < navMeshConfig.Profiles.Count; i++)
        {
            string id = navMeshConfig.Profiles[i]?.Id ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id) || !profileIds.Add(id))
            {
                throw new InvalidOperationException($"Mass-nav bake/data diagnostics found duplicate or empty navmesh profile id '{id}'.");
            }
        }

        var layers = new HashSet<int>();
        for (int i = 0; i < navMeshConfig.Layers.Count; i++)
        {
            if (!layers.Add(navMeshConfig.Layers[i].Layer))
            {
                throw new InvalidOperationException($"Mass-nav bake/data diagnostics found duplicate navmesh layer {navMeshConfig.Layers[i].Layer}.");
            }
        }

        var areaIds = new HashSet<int>();
        for (int i = 0; i < navMeshConfig.Areas.Count; i++)
        {
            int areaId = navMeshConfig.Areas[i].AreaId;
            if (areaId < 0 || areaId > 255 || !areaIds.Add(areaId))
            {
                throw new InvalidOperationException($"Mass-nav bake/data diagnostics found invalid or duplicate navmesh area id {areaId}.");
            }
        }

        for (int i = 0; i < pathingConfig.AgentTypes.Count; i++)
        {
            PathingAgentTypeConfig? agent = pathingConfig.AgentTypes[i];
            if (agent == null)
            {
                continue;
            }

            if (!profileIds.Contains(agent.ProfileId ?? string.Empty))
            {
                throw new InvalidOperationException($"Mass-nav pathing agent '{agent.Id}' references unknown navmesh profile '{agent.ProfileId}'.");
            }

            if (!layers.Contains(agent.Layer))
            {
                throw new InvalidOperationException($"Mass-nav pathing agent '{agent.Id}' references unknown navmesh layer {agent.Layer}.");
            }

            if (agent.NavMesh?.AreaCosts == null)
            {
                continue;
            }

            for (int c = 0; c < agent.NavMesh.AreaCosts.Count; c++)
            {
                int areaId = agent.NavMesh.AreaCosts[c].AreaId;
                if (!areaIds.Contains(areaId))
                {
                    throw new InvalidOperationException($"Mass-nav pathing agent '{agent.Id}' references unknown navmesh area {areaId}.");
                }
            }
        }
    }
}
