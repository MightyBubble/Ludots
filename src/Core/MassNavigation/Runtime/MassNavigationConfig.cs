using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationConfig
{
    [JsonRequired] public MassNavigationWorldConfig? World { get; set; }
    [JsonRequired] public MassNavigationFlowSolverConfig Solver { get; set; } = new();
    [JsonRequired] public MassNavigationCapacityConfig Capacity { get; set; } = new();
    [JsonRequired] public MassNavigationCadenceConfig Cadence { get; set; } = new();
    [JsonRequired] public MassNavigationAgentProfileSetConfig AgentProfiles { get; set; } = new();
    [JsonRequired] public MassNavigationFlowConfig Flow { get; set; } = new();
    [JsonRequired] public MassNavigationFlowArrivalTuning Arrival { get; set; } = new();
    [JsonRequired] public MassNavigationFlowAvoidanceTuning Avoidance { get; set; } = new();
    [JsonRequired] public MassNavigationCrowdSemantics Semantics { get; set; } = new();
    [JsonRequired] public MassNavigationStreamingConfig Streaming { get; set; } = new();

    public static MassNavigationConfig Load(JsonObject configObject)
    {
        if (configObject == null)
        {
            throw new ArgumentNullException(nameof(configObject));
        }

        using var document = JsonDocument.Parse(configObject.ToJsonString());
        return Load(document.RootElement);
    }

    public static MassNavigationConfig Load(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var document = JsonDocument.Parse(stream);
        return Load(document.RootElement);
    }

    private static MassNavigationConfig Load(JsonElement root)
    {
        var options = StrictJsonOptions.CreateCamelCase();

        MassNavigationConfig? config = root.Deserialize<MassNavigationConfig>(options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize mass-navigation config.");
        }

        config.Validate();
        return config;
    }

    internal void Validate()
    {
        Solver.Validate();
        Capacity.Validate();
        Cadence.Validate();
        AgentProfiles.Validate();
        Streaming.Validate();
        Flow.Validate();
        if (Flow.CrowdCostEnabled && Cadence.FlowCrowdStampHz == 0)
        {
            throw new InvalidOperationException(
                "MassNavigation cadence.flowCrowdStampHz must be positive when flow.crowdCostEnabled is true.");
        }

        Arrival.Validate();
        Avoidance.Validate();
        Semantics.Validate();
        float simulationStepSeconds = 1f / Cadence.SimulationHz;
        if (simulationStepSeconds > Semantics.Solver.MaxStepDtSeconds + 0.000001f)
        {
            throw new InvalidOperationException(
                $"MassNavigation cadence.simulationHz {Cadence.SimulationHz} produces a {simulationStepSeconds:0.######}s step, " +
                $"which exceeds semantics.solver.maxStepDtSeconds {Semantics.Solver.MaxStepDtSeconds:0.######} and would silently lose simulation time.");
        }

        if (World == null)
        {
            throw new InvalidOperationException("MassNavigation config requires an explicit world section.");
        }

        World.Validate(Solver);
        Capacity.ValidateForStreaming(World, Streaming);

    }
}

public sealed class MassNavigationCapacityConfig
{
    [JsonRequired] public int InitialCommandActorScratchCapacity { get; set; }
    [JsonRequired] public int InitialCommandActorSnapshotCapacity { get; set; }
    [JsonRequired] public int NavigationGroupCapacity { get; set; }
    [JsonRequired] public int GroupMembershipAgentCapacity { get; set; }
    [JsonRequired] public int CommandActorScratchCapacity { get; set; }
    [JsonRequired] public int GroupMemberCapacity { get; set; }
    [JsonRequired] public int OrderIngestionTokenCapacity { get; set; }
    [JsonRequired] public int OrderIngestionMemberCapacity { get; set; }
    [JsonRequired] public int RouteWaypointCapacityPerAgent { get; set; }
    [JsonRequired] public int LoadedChunkCapacity { get; set; }
    [JsonRequired] public int MetadataTeamCapacity { get; set; }
    [JsonRequired] public int FlowStateCapacity { get; set; }

    public void Validate()
    {
        RequirePositive(InitialCommandActorScratchCapacity, "initialCommandActorScratchCapacity");
        RequirePositive(InitialCommandActorSnapshotCapacity, "initialCommandActorSnapshotCapacity");
        RequirePositive(NavigationGroupCapacity, "navigationGroupCapacity");
        RequirePositive(GroupMembershipAgentCapacity, "groupMembershipAgentCapacity");
        RequirePositive(CommandActorScratchCapacity, "commandActorScratchCapacity");
        RequirePositive(GroupMemberCapacity, "groupMemberCapacity");
        RequirePositive(OrderIngestionTokenCapacity, "orderIngestionTokenCapacity");
        RequirePositive(OrderIngestionMemberCapacity, "orderIngestionMemberCapacity");
        RequirePositive(RouteWaypointCapacityPerAgent, "routeWaypointCapacityPerAgent");
        RequirePositive(LoadedChunkCapacity, "loadedChunkCapacity");
        RequirePositive(MetadataTeamCapacity, "metadataTeamCapacity");
        RequirePositive(FlowStateCapacity, "flowStateCapacity");

        if (CommandActorScratchCapacity < InitialCommandActorSnapshotCapacity)
        {
            throw new InvalidOperationException(
                "MassNavigation runtime.capacity.commandActorScratchCapacity must be >= runtime.capacity.initialCommandActorSnapshotCapacity.");
        }

        if (GroupMemberCapacity < InitialCommandActorSnapshotCapacity)
        {
            throw new InvalidOperationException(
                "MassNavigation runtime.capacity.groupMemberCapacity must be >= runtime.capacity.initialCommandActorSnapshotCapacity.");
        }

        if (OrderIngestionTokenCapacity < NavigationGroupCapacity)
        {
            throw new InvalidOperationException(
                "MassNavigation runtime.capacity.orderIngestionTokenCapacity must be >= runtime.capacity.navigationGroupCapacity.");
        }

        if (OrderIngestionMemberCapacity < InitialCommandActorSnapshotCapacity)
        {
            throw new InvalidOperationException(
                "MassNavigation runtime.capacity.orderIngestionMemberCapacity must be >= runtime.capacity.initialCommandActorSnapshotCapacity.");
        }

        try
        {
            _ = checked(GroupMembershipAgentCapacity * RouteWaypointCapacityPerAgent);
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException(
                "MassNavigation runtime.capacity route waypoint storage exceeds the supported contiguous array size.",
                exception);
        }
    }

    public void ValidateForScenario(int teamCount, int agentsPerTeam)
    {
        if (teamCount <= 0)
        {
            throw new InvalidOperationException("MassNavigation runtime.capacity scene validation requires a positive team count.");
        }

        long authoredAgentCount = (long)teamCount * agentsPerTeam;
        if (authoredAgentCount > GroupMembershipAgentCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime.capacity.groupMembershipAgentCapacity {GroupMembershipAgentCapacity} is smaller than authored scene agent count {authoredAgentCount}.");
        }

        if (authoredAgentCount > CommandActorScratchCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime.capacity.commandActorScratchCapacity {CommandActorScratchCapacity} is smaller than authored scene agent count {authoredAgentCount}.");
        }

        if (authoredAgentCount > GroupMemberCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime.capacity.groupMemberCapacity {GroupMemberCapacity} is smaller than authored scene agent count {authoredAgentCount}.");
        }

        if (authoredAgentCount > OrderIngestionMemberCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime.capacity.orderIngestionMemberCapacity {OrderIngestionMemberCapacity} is smaller than authored scene agent count {authoredAgentCount}.");
        }

        if (teamCount > MetadataTeamCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime.capacity.metadataTeamCapacity {MetadataTeamCapacity} is smaller than authored scene team count {teamCount}.");
        }

        if (teamCount > FlowStateCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime.capacity.flowStateCapacity {FlowStateCapacity} is smaller than authored scene team count {teamCount}.");
        }
    }

    public void ValidateForStreaming(MassNavigationWorldConfig world, MassNavigationStreamingConfig streaming)
    {
        if (world == null)
        {
            throw new InvalidOperationException("MassNavigation runtime.capacity streaming validation requires world config.");
        }

        if (streaming == null)
        {
            throw new InvalidOperationException("MassNavigation runtime.capacity streaming validation requires streaming config.");
        }

        int minimumWindowChunkCapacity = CountSquareChunksForRadius(
            streaming.RadiusCm,
            world.StreamingChunkSizeCm);
        if (LoadedChunkCapacity < minimumWindowChunkCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime.capacity.loadedChunkCapacity {LoadedChunkCapacity} is smaller than one streaming window chunk count {minimumWindowChunkCapacity}.");
        }
    }

    private static void RequirePositive(int value, string fieldName)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"MassNavigation runtime.capacity.{fieldName} must be > 0.");
        }
    }

    private static int CountSquareChunksForRadius(int radiusCm, int chunkSizeCm)
    {
        if (radiusCm <= 0 || chunkSizeCm <= 0)
        {
            throw new InvalidOperationException("MassNavigation streaming chunk capacity validation requires positive radius and chunk size.");
        }

        int chunkRadius = (radiusCm + chunkSizeCm - 1) / chunkSizeCm;
        int span = checked((chunkRadius * 2) + 1);
        return checked(span * span);
    }
}

public sealed class MassNavigationConfigLoader
{
    public const string DefaultRelativePath = "MassNavigationConfig.json";
    public const string MapMetadataSection = "massNavigation";
    public const string MapMetadataProfileId = "profileId";
    private const string ProfileIdField = "id";
    private const string ProfileExtendsField = "extends";

    private readonly ConfigPipeline _pipeline;

    public MassNavigationConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public MassNavigationCapabilityProfile Load(
        ConfigCatalog catalog,
        ConfigConflictReport report,
        MapConfig mapConfig,
        string relativePath = DefaultRelativePath)
    {
        if (TryLoad(catalog, report, mapConfig, out MassNavigationCapabilityProfile? config, relativePath))
        {
            return config;
        }

        throw new InvalidOperationException(
            $"Map '{mapConfig?.Id}' must declare metadata.{MapMetadataSection}.{MapMetadataProfileId} to activate MassNavigation.");
    }

    public bool TryLoad(
        ConfigCatalog catalog,
        ConfigConflictReport report,
        MapConfig mapConfig,
        out MassNavigationCapabilityProfile? config,
        string relativePath = DefaultRelativePath)
    {
        config = null;
        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (mapConfig == null)
        {
            throw new ArgumentNullException(nameof(mapConfig));
        }

        if (!TryResolveProfileId(mapConfig, out string profileId))
        {
            return false;
        }

        if (!catalog.TryGet(relativePath, out ConfigCatalogEntry entry))
        {
            throw new InvalidOperationException(
                $"MassNavigation profile catalog '{relativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.ArrayById ||
            !string.Equals(entry.IdField, ProfileIdField, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MassNavigation profile catalog '{relativePath}' must use ArrayById merge policy with IdField '{ProfileIdField}'.");
        }

        IReadOnlyList<MergedConfigEntry> mergedProfiles = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
        if (mergedProfiles.Count == 0)
        {
            throw new InvalidOperationException(
                $"MassNavigation profile catalog '{relativePath}' did not produce any profiles through ConfigPipeline.");
        }

        JsonObject resolved = ResolveProfile(mergedProfiles, profileId, relativePath);
        config = MassNavigationCapabilityProfile.Load(resolved);
        return true;
    }

    private static bool TryResolveProfileId(MapConfig mapConfig, out string profileId)
    {
        profileId = string.Empty;
        if (!mapConfig.Metadata.TryGetValue(MapMetadataSection, out JsonNode? sectionNode))
        {
            return false;
        }

        if (sectionNode is not JsonObject section)
        {
            throw new InvalidOperationException(
                $"Map '{mapConfig.Id}' metadata.{MapMetadataSection} must be an object.");
        }

        foreach ((string key, _) in section)
        {
            if (!string.Equals(key, MapMetadataProfileId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Map '{mapConfig.Id}' metadata.{MapMetadataSection} contains unknown property '{key}'.");
            }
        }

        if (!section.TryGetPropertyValue(MapMetadataProfileId, out JsonNode? profileNode) ||
            profileNode is not JsonValue profileValue ||
            !profileValue.TryGetValue(out profileId) ||
            string.IsNullOrWhiteSpace(profileId))
        {
            throw new InvalidOperationException(
                $"Map '{mapConfig.Id}' metadata.{MapMetadataSection}.{MapMetadataProfileId} must be a non-empty string.");
        }

        return true;
    }

    private static JsonObject ResolveProfile(
        IReadOnlyList<MergedConfigEntry> profiles,
        string profileId,
        string relativePath)
    {
        var profilesById = new Dictionary<string, JsonObject>(profiles.Count, StringComparer.Ordinal);
        for (int i = 0; i < profiles.Count; i++)
        {
            profilesById.Add(profiles[i].Id, profiles[i].Node);
        }

        if (!profilesById.ContainsKey(profileId))
        {
            throw new InvalidOperationException(
                $"MassNavigation profile '{profileId}' bound by the map was not found in '{relativePath}'.");
        }

        var chain = new List<JsonObject>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        string currentId = profileId;
        while (true)
        {
            if (!visiting.Add(currentId))
            {
                throw new InvalidOperationException(
                    $"MassNavigation profile inheritance contains a cycle at '{currentId}' in '{relativePath}'.");
            }

            JsonObject current = profilesById.TryGetValue(currentId, out JsonObject? found)
                ? found
                : throw new InvalidOperationException(
                    $"MassNavigation profile '{profileId}' extends missing profile '{currentId}' in '{relativePath}'.");
            chain.Add(current);
            if (!TryReadExtends(current, out string parentId))
            {
                break;
            }

            currentId = parentId;
        }

        var resolved = new JsonObject();
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            JsonObject layer = (JsonObject)chain[i].DeepClone();
            layer.Remove(ProfileIdField);
            layer.Remove(ProfileExtendsField);
            ConfigPipeline.DeepMerge(resolved, layer);
        }

        return resolved;
    }

    private static bool TryReadExtends(JsonObject profile, out string parentId)
    {
        parentId = string.Empty;
        if (!profile.TryGetPropertyValue(ProfileExtendsField, out JsonNode? node) || node == null)
        {
            return false;
        }

        if (node is not JsonValue value ||
            !value.TryGetValue(out parentId) ||
            string.IsNullOrWhiteSpace(parentId))
        {
            throw new InvalidOperationException(
                $"MassNavigation profile '{profile[ProfileIdField]}' extends must be a non-empty string.");
        }

        return true;
    }
}

public sealed class MassNavigationStreamingConfig
{
    [JsonRequired] public float RetainSeconds { get; set; }
    [JsonRequired] public int RadiusCm { get; set; }

    public void Validate()
    {
        if (RetainSeconds < 0f)
        {
            throw new InvalidOperationException("MassNavigation streaming.retainSeconds must be >= 0.");
        }

        if (RadiusCm <= 0)
        {
            throw new InvalidOperationException("MassNavigation streaming.radiusCm must be > 0.");
        }
    }
}

public sealed class MassNavigationPresentationConfig
{
    [JsonRequired] public string[] RequiredMeshAssetIds { get; set; } = Array.Empty<string>();
    [JsonRequired] public string? BlockerPerformerId { get; set; }
    [JsonRequired] public string? HotspotPerformerId { get; set; }
    [JsonRequired] public string BlockerTemplateId { get; set; } = string.Empty;
    [JsonRequired] public string? HotspotTemplateId { get; set; }
    [JsonRequired] public MassNavigationTeamPresentationConfig[] Teams { get; set; } = Array.Empty<MassNavigationTeamPresentationConfig>();

    public void Validate(MassNavigationScenarioConfig scenario, MassNavigationWorldConfig? world)
    {
        if (RequiredMeshAssetIds.Length <= 0)
        {
            throw new InvalidOperationException("MassNavigation presentation requires at least one RequiredMeshAssetIds entry.");
        }

        var meshIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < RequiredMeshAssetIds.Length; i++)
        {
            string meshAssetId = RequiredMeshAssetIds[i];
            RequireNonEmpty(meshAssetId, $"{nameof(RequiredMeshAssetIds)}[{i}]");
            if (!meshIds.Add(meshAssetId))
            {
                throw new InvalidOperationException($"MassNavigation presentation contains duplicate required mesh asset '{meshAssetId}'.");
            }
        }

        RequireNonEmpty(BlockerPerformerId, nameof(BlockerPerformerId));
        RequireNonEmpty(HotspotPerformerId, nameof(HotspotPerformerId));
        RequireNonEmpty(HotspotTemplateId, nameof(HotspotTemplateId));

        if (Teams.Length != scenario.Teams.Length)
        {
            throw new InvalidOperationException("MassNavigation presentation team style count must match scenario teams.");
        }

        var scenarioTeamIds = new HashSet<int>(scenario.Teams.Length);
        for (int i = 0; i < scenario.Teams.Length; i++)
        {
            scenarioTeamIds.Add(scenario.Teams[i].Id);
        }

        var seenIds = new HashSet<int>(Teams.Length);
        for (int i = 0; i < Teams.Length; i++)
        {
            MassNavigationTeamPresentationConfig team = Teams[i];
            team.Validate();
            if (!scenarioTeamIds.Contains(team.TeamId))
            {
                throw new InvalidOperationException($"MassNavigation presentation team style references unknown team {team.TeamId}.");
            }

            if (!seenIds.Add(team.TeamId))
            {
                throw new InvalidOperationException($"MassNavigation presentation contains duplicate style for team {team.TeamId}.");
            }
        }
    }

    public MassNavigationTeamPresentationConfig GetTeam(int teamId)
    {
        for (int i = 0; i < Teams.Length; i++)
        {
            if (Teams[i].TeamId == teamId)
            {
                return Teams[i];
            }
        }

        throw new InvalidOperationException($"MassNavigation presentation missing team style for team {teamId}.");
    }

    public string ResolveAgentTemplateId(int teamId, bool heavy)
    {
        MassNavigationTeamPresentationConfig team = GetTeam(teamId);
        return heavy ? team.HeavyTemplateId : team.LightTemplateId;
    }

    public string ResolveAgentPerformerId(int teamId, bool heavy)
    {
        MassNavigationTeamPresentationConfig team = GetTeam(teamId);
        return heavy ? team.HeavyPerformerId : team.LightPerformerId;
    }

    private static void RequireNonEmpty(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"MassNavigation presentation requires non-empty {fieldName}.");
        }
    }
}

public sealed class MassNavigationFlowSolverConfig
{
    [JsonRequired] public int FieldWidthCm { get; set; }
    [JsonRequired] public int FieldHeightCm { get; set; }
    [JsonRequired] public int FlowCellSizeCm { get; set; }
    [JsonRequired] public int MaxObstacleCount { get; set; }
    [JsonRequired] public int ParallelWorkerCount { get; set; }
    [JsonRequired] public int SeparationHashCellSizeCm { get; set; }
    [JsonRequired] public int SeparationHashMinSearchRadiusCells { get; set; }
    [JsonRequired] public int HardResolveHashCellSizeCm { get; set; }
    [JsonRequired] public int HardResolveHashMinSearchRadiusCells { get; set; }
    [JsonRequired] public float PlayAreaMinXCm { get; set; }
    [JsonRequired] public float PlayAreaMaxXCm { get; set; }
    [JsonRequired] public float PlayAreaMinYCm { get; set; }
    [JsonRequired] public float PlayAreaMaxYCm { get; set; }

    [JsonIgnore]
    public int FlowGridWidth => FieldWidthCm / FlowCellSizeCm;

    [JsonIgnore]
    public int FlowGridHeight => FieldHeightCm / FlowCellSizeCm;

    [JsonIgnore]
    public int SeparationHashWidth => FieldWidthCm / SeparationHashCellSizeCm;

    [JsonIgnore]
    public int SeparationHashHeight => FieldHeightCm / SeparationHashCellSizeCm;

    [JsonIgnore]
    public int HardResolveHashWidth => FieldWidthCm / HardResolveHashCellSizeCm;

    [JsonIgnore]
    public int HardResolveHashHeight => FieldHeightCm / HardResolveHashCellSizeCm;

    public void Validate()
    {
        RequirePositive(FieldWidthCm, nameof(FieldWidthCm));
        RequirePositive(FieldHeightCm, nameof(FieldHeightCm));
        RequirePositive(FlowCellSizeCm, nameof(FlowCellSizeCm));
        RequirePositive(MaxObstacleCount, nameof(MaxObstacleCount));
        RequirePositive(ParallelWorkerCount, nameof(ParallelWorkerCount));
        RequirePositive(SeparationHashCellSizeCm, nameof(SeparationHashCellSizeCm));
        RequireNonNegative(SeparationHashMinSearchRadiusCells, nameof(SeparationHashMinSearchRadiusCells));
        RequirePositive(HardResolveHashCellSizeCm, nameof(HardResolveHashCellSizeCm));
        RequireNonNegative(HardResolveHashMinSearchRadiusCells, nameof(HardResolveHashMinSearchRadiusCells));
        RequireDivisible(FieldWidthCm, FlowCellSizeCm, nameof(FieldWidthCm), nameof(FlowCellSizeCm));
        RequireDivisible(FieldHeightCm, FlowCellSizeCm, nameof(FieldHeightCm), nameof(FlowCellSizeCm));
        RequireDivisible(FieldWidthCm, SeparationHashCellSizeCm, nameof(FieldWidthCm), nameof(SeparationHashCellSizeCm));
        RequireDivisible(FieldHeightCm, SeparationHashCellSizeCm, nameof(FieldHeightCm), nameof(SeparationHashCellSizeCm));
        RequireDivisible(FieldWidthCm, HardResolveHashCellSizeCm, nameof(FieldWidthCm), nameof(HardResolveHashCellSizeCm));
        RequireDivisible(FieldHeightCm, HardResolveHashCellSizeCm, nameof(FieldHeightCm), nameof(HardResolveHashCellSizeCm));
        RequireGridCapacity(FlowGridWidth, FlowGridHeight, "flow grid");
        RequireGridCapacity(SeparationHashWidth, SeparationHashHeight, "separation hash");
        RequireGridCapacity(HardResolveHashWidth, HardResolveHashHeight, "hard-resolve hash");
        RequireOrderedPlayArea(PlayAreaMinXCm, PlayAreaMaxXCm, FieldWidthCm, nameof(PlayAreaMinXCm), nameof(PlayAreaMaxXCm));
        RequireOrderedPlayArea(PlayAreaMinYCm, PlayAreaMaxYCm, FieldHeightCm, nameof(PlayAreaMinYCm), nameof(PlayAreaMaxYCm));
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"MassNavigation solver requires {name} > 0.");
        }
    }

    private static void RequireNonNegative(int value, string name)
    {
        if (value < 0)
        {
            throw new InvalidOperationException($"MassNavigation solver requires {name} >= 0.");
        }
    }

    private static void RequireDivisible(int value, int divisor, string valueName, string divisorName)
    {
        if (value % divisor != 0)
        {
            throw new InvalidOperationException($"MassNavigation solver requires {valueName} to be divisible by {divisorName}.");
        }
    }

    private static void RequireGridCapacity(int width, int height, string label)
    {
        if ((long)width * height > int.MaxValue)
        {
            throw new InvalidOperationException($"MassNavigation solver {label} is too large for managed SoA arrays.");
        }
    }

    private static void RequireOrderedPlayArea(float min, float max, int fieldSize, string minName, string maxName)
    {
        if (!(min >= 0f) || !(max <= fieldSize) || !(min <= max))
        {
            throw new InvalidOperationException(
                $"MassNavigation solver requires ordered {minName}/{maxName} inside the configured field.");
        }
    }
}

public sealed class MassNavigationTeamPresentationConfig
{
    [JsonRequired] public int TeamId { get; set; }
    [JsonRequired] public string StyleId { get; set; } = string.Empty;
    [JsonRequired] public string LightTemplateId { get; set; } = string.Empty;
    [JsonRequired] public string HeavyTemplateId { get; set; } = string.Empty;
    [JsonRequired] public string LightPerformerId { get; set; } = string.Empty;
    [JsonRequired] public string HeavyPerformerId { get; set; } = string.Empty;

    public void Validate()
    {
        if (TeamId <= 0)
        {
            throw new InvalidOperationException("MassNavigation presentation team style requires TeamId > 0.");
        }

        if (string.IsNullOrWhiteSpace(StyleId))
        {
            throw new InvalidOperationException($"MassNavigation presentation team {TeamId} requires StyleId.");
        }

        if (string.IsNullOrWhiteSpace(LightTemplateId))
        {
            throw new InvalidOperationException($"MassNavigation presentation team {TeamId} requires LightTemplateId.");
        }

        if (string.IsNullOrWhiteSpace(HeavyTemplateId))
        {
            throw new InvalidOperationException($"MassNavigation presentation team {TeamId} requires HeavyTemplateId.");
        }

        if (string.IsNullOrWhiteSpace(LightPerformerId))
        {
            throw new InvalidOperationException($"MassNavigation presentation team {TeamId} requires LightPerformerId.");
        }

        if (string.IsNullOrWhiteSpace(HeavyPerformerId))
        {
            throw new InvalidOperationException($"MassNavigation presentation team {TeamId} requires HeavyPerformerId.");
        }
    }
}

public sealed class MassNavigationWorldConfig
{
    private int _activeHotZoneIndex = -1;

    [JsonRequired] public int StreamingChunkSizeCm { get; set; }
    [JsonRequired] public int CommandFocusHoldTicks { get; set; }
    [JsonRequired] public int WorkAreaPaddingCm { get; set; }
    [JsonRequired] public int WorkAreaMaxWidthCm { get; set; }
    [JsonRequired] public int WorkAreaMaxHeightCm { get; set; }
    [JsonRequired] public string ActiveHotZoneId { get; set; } = string.Empty;
    [JsonRequired] public MassNavigationHotZoneConfig[] HotZones { get; set; } = Array.Empty<MassNavigationHotZoneConfig>();

    [JsonIgnore]
    public MassNavigationHotZoneConfig ActiveHotZone => _activeHotZoneIndex >= 0 && _activeHotZoneIndex < HotZones.Length
        ? HotZones[_activeHotZoneIndex]
        : throw new InvalidOperationException("MassNavigation world active hot zone was not validated.");

    [JsonIgnore]
    public int HotZoneCenterXCm => ActiveHotZone.CenterXCm;

    [JsonIgnore]
    public int HotZoneCenterYCm => ActiveHotZone.CenterYCm;

    [JsonIgnore]
    public string ActiveHotZoneLabel => ActiveHotZone.Label;

    public void SetActiveHotZone(string hotZoneId)
    {
        int index = FindHotZoneIndex(hotZoneId);
        if (index < 0)
        {
            throw new InvalidOperationException($"MassNavigation world hot zone '{hotZoneId}' is not configured.");
        }

        _activeHotZoneIndex = index;
        ActiveHotZoneId = HotZones[index].Id;
    }

    public bool TryGetHotZone(string hotZoneId, out MassNavigationHotZoneConfig hotZone)
    {
        int index = FindHotZoneIndex(hotZoneId);
        if (index < 0)
        {
            hotZone = null!;
            return false;
        }

        hotZone = HotZones[index];
        return true;
    }

    public void Validate(MassNavigationFlowSolverConfig solver)
    {
        if (solver == null)
        {
            throw new InvalidOperationException("MassNavigation world validation requires an explicit solver section.");
        }

        if (HotZones.Length <= 0)
        {
            throw new InvalidOperationException("MassNavigation world requires at least one configured hotspot debug landmark.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < HotZones.Length; i++)
        {
            MassNavigationHotZoneConfig zone = HotZones[i];
            zone.Validate();
            if (!ids.Add(zone.Id))
            {
                throw new InvalidOperationException($"MassNavigation world contains duplicate hot zone id '{zone.Id}'.");
            }

        }

        if (StreamingChunkSizeCm <= 0)
        {
            throw new InvalidOperationException("MassNavigation world requires StreamingChunkSizeCm > 0.");
        }

        if (CommandFocusHoldTicks < 0)
        {
            throw new InvalidOperationException("MassNavigation world requires CommandFocusHoldTicks >= 0.");
        }

        if (WorkAreaPaddingCm < 0)
        {
            throw new InvalidOperationException("MassNavigation world requires WorkAreaPaddingCm >= 0.");
        }

        if (WorkAreaMaxWidthCm <= 0 || WorkAreaMaxHeightCm <= 0)
        {
            throw new InvalidOperationException("MassNavigation world requires positive WorkAreaMaxWidthCm and WorkAreaMaxHeightCm.");
        }

        if (WorkAreaMaxWidthCm < solver.FieldWidthCm || WorkAreaMaxHeightCm < solver.FieldHeightCm)
        {
            throw new InvalidOperationException("MassNavigation world work area max must be at least the solver cache size.");
        }

        if (string.IsNullOrWhiteSpace(ActiveHotZoneId))
        {
            throw new InvalidOperationException("MassNavigation world requires ActiveHotZoneId as the initial hotspot debug landmark.");
        }

        SetActiveHotZone(ActiveHotZoneId);
    }

    private int FindHotZoneIndex(string hotZoneId)
    {
        for (int i = 0; i < HotZones.Length; i++)
        {
            if (string.Equals(HotZones[i].Id, hotZoneId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}

public sealed class MassNavigationHotZoneConfig
{
    [JsonRequired] public string Id { get; set; } = string.Empty;
    [JsonRequired] public string Label { get; set; } = string.Empty;
    [JsonRequired] public int CenterXCm { get; set; }
    [JsonRequired] public int CenterYCm { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException("MassNavigation hot zone requires a non-empty id.");
        }

        if (string.IsNullOrWhiteSpace(Label))
        {
            throw new InvalidOperationException($"MassNavigation hot zone '{Id}' requires a non-empty label.");
        }

    }
}

public sealed class MassNavigationScenarioConfig
{
    [JsonRequired] public int AgentsPerTeam { get; set; }
    [JsonRequired] public int InitialActiveTeamId { get; set; }
    [JsonRequired] public MassNavigationScenarioTeamConfig[] Teams { get; set; } = Array.Empty<MassNavigationScenarioTeamConfig>();
    [JsonRequired] public MassNavigationScenarioSpawnLayoutConfig? SpawnLayout { get; set; }

    public void Validate(MassNavigationCapacityConfig capacity)
    {
        if (capacity == null)
        {
            throw new InvalidOperationException("MassNavigation scene validation requires runtime.capacity.");
        }

        if (AgentsPerTeam < 0)
        {
            throw new InvalidOperationException("MassNavigation config requires AgentsPerTeam >= 0.");
        }

        if (Teams.Length <= 0)
        {
            throw new InvalidOperationException("MassNavigation config requires at least one team.");
        }

        var seenIds = new HashSet<int>(Teams.Length);
        for (int i = 0; i < Teams.Length; i++)
        {
            MassNavigationScenarioTeamConfig team = Teams[i];
            if (team.Id <= 0)
            {
                throw new InvalidOperationException("MassNavigation config team ids must be positive.");
            }

            if (string.IsNullOrWhiteSpace(team.Name))
            {
                throw new InvalidOperationException($"MassNavigation config team {team.Id} requires a name.");
            }

            if (!seenIds.Add(team.Id))
            {
                throw new InvalidOperationException($"MassNavigation config contains duplicate team id {team.Id}.");
            }
        }

        if (!seenIds.Contains(InitialActiveTeamId))
        {
            throw new InvalidOperationException(
                $"MassNavigation config InitialActiveTeamId {InitialActiveTeamId} is not present in Scenario.Teams.");
        }

        long authoredAgentCount = (long)Teams.Length * AgentsPerTeam;
        if (authoredAgentCount > capacity.InitialCommandActorScratchCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime.capacity.initialCommandActorScratchCapacity {capacity.InitialCommandActorScratchCapacity} is smaller than authored scene agent count {authoredAgentCount}.");
        }

        if (authoredAgentCount > capacity.InitialCommandActorSnapshotCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime.capacity.initialCommandActorSnapshotCapacity {capacity.InitialCommandActorSnapshotCapacity} is smaller than authored scene agent count {authoredAgentCount}.");
        }

        capacity.ValidateForScenario(Teams.Length, AgentsPerTeam);

        SpawnLayout?.Validate();
    }
}

public enum MassNavigationScenarioSpawnLayoutKind : byte
{
    OrbitOpposedTargets = 1,
}

public sealed class MassNavigationScenarioSpawnLayoutConfig
{
    private MassNavigationScenarioSpawnLayoutKind _parsedKind;

    [JsonRequired] public string Kind { get; set; } = string.Empty;
    [JsonRequired] public float OrbitRadiusCm { get; set; }
    [JsonRequired] public int RandomSeed { get; set; }

    [JsonIgnore]
    public MassNavigationScenarioSpawnLayoutKind ParsedKind => _parsedKind;

    public void Validate()
    {
        _parsedKind = Kind switch
        {
            "OrbitOpposedTargets" => MassNavigationScenarioSpawnLayoutKind.OrbitOpposedTargets,
            "" => throw new InvalidOperationException("MassNavigation scenario.spawnLayout.kind must be a non-empty semantic string."),
            _ => throw new InvalidOperationException(
                $"MassNavigation scenario.spawnLayout.kind '{Kind}' is not configured.")
        };

        if (OrbitRadiusCm <= 0f)
        {
            throw new InvalidOperationException("MassNavigation scenario.spawnLayout.orbitRadiusCm must be > 0.");
        }
    }
}

public sealed class MassNavigationScenarioTeamConfig
{
    [JsonRequired] public int Id { get; set; }
    [JsonRequired] public string Name { get; set; } = string.Empty;
}
