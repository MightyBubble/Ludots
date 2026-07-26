using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Teams;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationConfig
{
    public string MapId { get; set; } = string.Empty;
    public string[] CompatibleMapIds { get; set; } = Array.Empty<string>();
    public MassNavigationWorldConfig? World { get; set; }
    public MassNavigationFlowSolverConfig Solver { get; set; } = new();
    public MassNavigationPresentationConfig Presentation { get; set; } = new();
    public MassNavigationScenarioConfig Scenario { get; set; } = new();
    public MassNavigationScenarioRuntimeConfig ScenarioRuntime { get; set; } = new();
    public MassNavigationCadenceConfig Cadence { get; set; } = new();
    public MassNavigationAgentProfileSetConfig AgentProfiles { get; set; } = new();
    public TeamConfig TeamRelationships { get; set; } = new();
    public MassNavigationRelationshipPolicyConfig RelationshipPolicy { get; set; } = new();
    public MassNavigationFlowTuning Flow { get; set; } = new();
    public MassNavigationFlowArrivalTuning Arrival { get; set; } = new();
    public MassNavigationFlowAvoidanceTuning Avoidance { get; set; } = new();
    public MassNavigationCrowdSemantics Semantics { get; set; } = new();
    public MassNavigationStreamingConfig Streaming { get; set; } = new();

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
        ValidateRequiredTopLevelProperties(root);
        var options = StrictJsonOptions.CreateCamelCase();

        MassNavigationConfig? config = root.Deserialize<MassNavigationConfig>(options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize mass-navigation config.");
        }

        config.Validate();
        return config;
    }

    private static void ValidateRequiredTopLevelProperties(JsonElement root)
    {
        RequireProperty(root, "mapId");
        RequireProperty(root, "world");
        RequireProperty(root, "solver");
        RequireProperty(root, "presentation");
        RequireProperty(root, "scenario");
        RequireProperty(root, "scenarioRuntime");
        RequireProperty(root, "cadence");
        RequireProperty(root, "agentProfiles");
        RequireProperty(root, "teamRelationships");
        RequireProperty(root, "relationshipPolicy");
        RequireProperty(root, "flow");
        RequireProperty(root, "arrival");
        RequireProperty(root, "avoidance");
        RequireProperty(root, "semantics");
        RequireProperty(root, "streaming");
        JsonElement scenarioRuntime = RequireProperty(root, "scenarioRuntime");
        bool autoSpawnConfiguredScenario = RequireBooleanProperty(scenarioRuntime, "autoSpawnConfiguredScenario");
        RequireProperties(
            scenarioRuntime,
            "runtimeCapacity");
        RequireProperties(
            RequireProperty(scenarioRuntime, "runtimeCapacity"),
            "navigationGroupCapacity",
            "groupMembershipAgentCapacity",
            "groupMemberCapacity",
            "movePlanExecutionGroupCapacity",
            "movePlanExecutionMemberCapacity",
            "routeStateCapacity",
            "routeMaxExpandedPerRequest",
            "routeWaypointCapacityPerAgent",
            "loadedChunkCapacity",
            "relationshipDomainCapacity",
            "displacedAgentCapacity");

        JsonElement world = RequireProperty(root, "world");
        RequireProperties(
            world,
            "solverWindowWidthCm",
            "solverWindowHeightCm",
            "streamingChunkSizeCm",
            "commandFocusHoldTicks",
            "workAreaPaddingCm",
            "workAreaMaxWidthCm",
            "workAreaMaxHeightCm",
            "activeHotZoneId",
            "hotZones");
        RequireProperties(
            RequireProperty(root, "solver"),
            "fieldWidthCm",
            "fieldHeightCm",
            "flowCellSizeCm",
            "maxObstacleCount",
            "parallelWorkerCount",
            "separationHashCellSizeCm",
            "separationHashMinSearchRadiusCells",
            "hardResolveHashCellSizeCm",
            "hardResolveHashMinSearchRadiusCells",
            "playAreaMinXCm",
            "playAreaMaxXCm",
            "playAreaMinYCm",
            "playAreaMaxYCm");
        JsonElement presentation = RequireProperty(root, "presentation");
        RequireProperties(
            presentation,
            "requiredMeshAssetIds",
            "blockerTemplateId",
            "teams");
        if (autoSpawnConfiguredScenario)
        {
            RequireProperties(
                presentation,
                "blockerPerformerId",
                "hotspotPerformerId",
                "hotspotTemplateId");
        }

        JsonElement scenario = RequireProperty(root, "scenario");
        RequireProperties(
            scenario,
            "agentsPerTeam",
            "teams");
        if (autoSpawnConfiguredScenario)
        {
            RequireProperties(
                RequireProperty(scenario, "spawnLayout"),
                "kind",
                "orbitRadiusCm",
                "randomSeed");
        }

        JsonElement cadence = RequireProperty(root, "cadence");
        RequireProperties(
            cadence,
            "simulationHz",
            "targetUpdateHz",
            "flowStepHz",
            "flowCrowdStampHz",
            "flowObstacleStampHz",
            "hardResolveHz",
            "entitySyncHz",
            "maxStepsPerFixedTick",
            "hardResolveCandidateThresholdAgents");
        JsonElement agentProfiles = RequireProperty(root, "agentProfiles");
        RequireProperties(agentProfiles, "defaultProfileId", "profiles");
        JsonElement profiles = RequireProperty(agentProfiles, "profiles");
        if (profiles.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("MassNavigation agentProfiles.profiles must be an explicit array.");
        }

        int profileIndex = 0;
        foreach (JsonElement profile in profiles.EnumerateArray())
        {
            RequireProperties(
                profile,
                "id",
                "heavy",
                "visualScale",
                "speedCmPerSecond",
                "everyNth",
                "nthOffset");
            profileIndex++;
        }

        if (profileIndex <= 0)
        {
            throw new InvalidOperationException("MassNavigation agentProfiles.profiles requires at least one explicit profile.");
        }

        JsonElement relationships = RequireProperty(root, "teamRelationships");
        RequireProperty(relationships, "defaultRelationship");
        RequireProperty(relationships, "relationships");
        RequireProperties(RequireProperty(root, "relationshipPolicy"), "cooperativeStance");
        RequireProperties(
            RequireProperty(root, "streaming"),
            "retainSeconds",
            "radiusCm");
        RequireProperties(
            RequireProperty(root, "flow"),
            "enabled",
            "iterationsPerStep",
            "maxIterationsPerStep",
            "forceRefreshFlow",
            "forceRefreshCrowd",
            "forceRefreshObstacles");
        RequireProperties(
            RequireProperty(root, "arrival"),
            "enabled",
            "timeoutMs",
            "timeoutMinMs",
            "timeoutMaxMs",
            "progressDistanceCm",
            "progressDistanceMinCm",
            "progressDistanceMaxCm",
            "wakePushDistanceCm",
            "wakePushDistanceMinCm",
            "wakePushDistanceMaxCm",
            "maxRetryCountMin",
            "maxRetryCountMax",
            "maxRetryCount");
        RequireProperties(
            RequireProperty(root, "avoidance"),
            "mode",
            "orca",
            "sonar",
            "dominantMassRatio",
            "friendlyResponseScale",
            "friendlyResponseMin",
            "friendlyResponseMax",
            "nonFriendlyResponseScale",
            "nonFriendlyResponseMin",
            "nonFriendlyResponseMax",
            "dominantPushResponseScale",
            "dominantPushResponseMin",
            "dominantPushResponseMax",
            "friendlyCorrectionShareMin",
            "friendlyCorrectionShareMax",
            "dominantCorrectionOtherMassWeight",
            "dominantCorrectionShareMin",
            "dominantCorrectionShareMax",
            "nonFriendlyCorrectionOtherMassWeight",
            "nonFriendlyCorrectionShareMin",
            "nonFriendlyCorrectionShareMax");
        RequireProperties(
            RequireProperty(RequireProperty(root, "avoidance"), "orca"),
            "timeHorizonSeconds",
            "maxNeighbors");
        RequireProperties(
            RequireProperty(RequireProperty(root, "avoidance"), "sonar"),
            "maxSteerAngleDeg",
            "backwardPenaltyAngleDeg",
            "predictionTimeScale",
            "ignoreBehindMovingAgents",
            "blockedStop",
            "usePreferredVelocityWhenBlocked",
            "timeHorizonSeconds",
            "maxNeighbors");
        JsonElement semantics = RequireProperty(root, "semantics");
        RequireProperties(
            RequireProperty(semantics, "obstacle"),
            "hardResolveCandidateDistanceCm",
            "softPushPaddingCm",
            "softPushForceScale");
        RequireProperties(
            RequireProperty(semantics, "targetProjection"),
            "teamTargetClearanceCm",
            "groupCenterClearanceCm",
            "teamSlotClearanceCm",
            "groupSlotClearanceCm",
            "looseTargetClearanceCm");
        RequireProperties(
            RequireProperty(semantics, "group"),
            "spawnSpacingCm",
            "spawnJitterCm",
            "teamSlotSpacingCm",
            "pullDeadZoneCm",
            "pullClampCm",
            "arrivedRadiusCm",
            "groupedAgentArriveThresholdCm",
            "looseArriveThresholdCm",
            "unitTargetStopThresholdCm",
            "groupedAgentFlowSlowRadiusCm",
            "nearSlotBlend",
            "farSlotBlend",
            "nearSlotBlendDistanceSq");
        RequireProperties(
            RequireProperty(semantics, "route"),
            "waypointAdvanceStopThresholdScale",
            "waypointAdvanceBodyRadiusScale");
        RequireProperties(
            RequireProperty(semantics, "steering"),
            "separationRadiusCm",
            "goalArrivalRadiusCm",
            "flowObstacleAvoidanceScale",
            "groupedAgentSeparationScale",
            "looseSeparationScale",
            "velocityBlendPerSecond");
        RequireProperties(
            RequireProperty(semantics, "solver"),
            "minNavMass",
            "minVisualScale",
            "maxStepDtSeconds",
            "parallelStepMinAgents",
            "directionEpsilonSq",
            "normalizationEpsilonSq",
            "inverseSqrtMinValue",
            "entitySyncPositionEpsilonSq",
            "entitySyncVelocityEpsilonSq",
            "facingVelocityEpsilonSq",
            "flowBlockedCellCost",
            "flowBlockedCellThreshold",
            "flowTargetStopDistanceSq",
            "flowObstacleNeighborRadiusCells",
            "flowObstacleNeighborWeight",
            "flowObstacleAvoidanceWeight",
            "crowdStampCenterCost",
            "crowdStampNeighborCost",
            "coincidentPairHashBucketCount",
            "coincidentPairHashPrimeA",
            "coincidentPairHashPrimeB");
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"MassNavigation config requires explicit '{propertyName}' property.");
        }

        return value;
    }

    private static bool RequireBooleanProperty(JsonElement root, string propertyName)
    {
        JsonElement value = RequireProperty(root, propertyName);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException($"MassNavigation config requires explicit boolean '{propertyName}' property.")
        };
    }

    private static void RequireProperties(JsonElement root, params string[] propertyNames)
    {
        for (int i = 0; i < propertyNames.Length; i++)
        {
            RequireProperty(root, propertyNames[i]);
        }
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(MapId))
        {
            throw new InvalidOperationException("MassNavigation config requires a non-empty map id.");
        }

        var supportedMapIds = new HashSet<string>(StringComparer.Ordinal) { MapId };
        for (int i = 0; i < CompatibleMapIds.Length; i++)
        {
            string mapId = CompatibleMapIds[i];
            if (string.IsNullOrWhiteSpace(mapId) || !string.Equals(mapId.Trim(), mapId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"MassNavigation compatibleMapIds[{i}] must be non-empty and trimmed.");
            }

            if (!supportedMapIds.Add(mapId))
            {
                throw new InvalidOperationException(
                    $"MassNavigation compatibleMapIds[{i}] duplicates supported map id '{mapId}'.");
            }
        }

        Solver.Validate();
        ScenarioRuntime.Validate();
        Scenario.Validate(ScenarioRuntime);
        Presentation.Validate(Scenario, ScenarioRuntime, World);
        Cadence.Validate();
        AgentProfiles.Validate();
        Streaming.Validate();
        Flow.Validate();
        Arrival.Validate();
        Avoidance.Validate();
        RelationshipPolicy.Validate();
        Semantics.Validate();
        if (World == null)
        {
            throw new InvalidOperationException("MassNavigation config requires an explicit world section.");
        }

        World.Validate(Solver);
        ScenarioRuntime.RuntimeCapacity.ValidateForStreaming(World, Streaming);

        ValidateRelationships();

        var knownTeams = new HashSet<int>(Scenario.Teams.Length);
        for (int i = 0; i < Scenario.Teams.Length; i++)
        {
            knownTeams.Add(Scenario.Teams[i].Id);
        }

        for (int i = 0; i < TeamRelationships.Relationships.Count; i++)
        {
            RelationshipEntry relation = TeamRelationships.Relationships[i];
            if (!knownTeams.Contains(relation.TeamA) || !knownTeams.Contains(relation.TeamB))
            {
                throw new InvalidOperationException(
                    $"MassNavigation config relationship [{relation.TeamA},{relation.TeamB}] references an unknown team.");
            }

            if (string.IsNullOrWhiteSpace(relation.Attitude))
            {
                throw new InvalidOperationException(
                    $"MassNavigation config relationship [{relation.TeamA},{relation.TeamB}] requires a stance key.");
            }
        }
    }

    public bool SupportsMap(string? mapId)
    {
        if (string.IsNullOrEmpty(mapId))
        {
            return false;
        }

        if (string.Equals(MapId, mapId, StringComparison.Ordinal))
        {
            return true;
        }

        for (int i = 0; i < CompatibleMapIds.Length; i++)
        {
            if (string.Equals(CompatibleMapIds[i], mapId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void ValidateRelationships()
    {
        if (TeamRelationships == null)
        {
            throw new InvalidOperationException("MassNavigation config requires an explicit teamRelationships section.");
        }

        if (string.IsNullOrWhiteSpace(TeamRelationships.DefaultRelationship))
        {
            throw new InvalidOperationException(
                "MassNavigation config teamRelationships.defaultRelationship requires a stance key.");
        }

        if (TeamRelationships.Relationships == null)
        {
            throw new InvalidOperationException("MassNavigation config requires teamRelationships.relationships as an explicit array.");
        }
    }
}

public sealed class MassNavigationRelationshipPolicyConfig
{
    public string CooperativeStance { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CooperativeStance))
        {
            throw new InvalidOperationException("MassNavigation relationshipPolicy.cooperativeStance must be explicit.");
        }
    }
}

public sealed class MassNavigationScenarioRuntimeConfig
{
    public bool AutoSpawnConfiguredScenario { get; set; }
    public MassNavigationRuntimeCapacityConfig RuntimeCapacity { get; set; } = new();

    public void Validate()
    {
        if (RuntimeCapacity == null)
        {
            throw new InvalidOperationException("MassNavigation scenarioRuntime.runtimeCapacity must be explicitly configured.");
        }

        RuntimeCapacity.Validate();
    }
}

public sealed class MassNavigationRuntimeCapacityConfig
{
    public int NavigationGroupCapacity { get; set; }
    public int GroupMembershipAgentCapacity { get; set; }
    public int GroupMemberCapacity { get; set; }
    public int MovePlanExecutionGroupCapacity { get; set; }
    public int MovePlanExecutionMemberCapacity { get; set; }
    public int RouteStateCapacity { get; set; }
    public int RouteMaxExpandedPerRequest { get; set; }
    public int RouteWaypointCapacityPerAgent { get; set; }
    public int LoadedChunkCapacity { get; set; }
    public int RelationshipDomainCapacity { get; set; }
    public int DisplacedAgentCapacity { get; set; }

    public void Validate()
    {
        RequirePositive(NavigationGroupCapacity, "navigationGroupCapacity");
        RequirePositive(GroupMembershipAgentCapacity, "groupMembershipAgentCapacity");
        RequirePositive(GroupMemberCapacity, "groupMemberCapacity");
        RequirePositive(MovePlanExecutionGroupCapacity, "movePlanExecutionGroupCapacity");
        RequirePositive(MovePlanExecutionMemberCapacity, "movePlanExecutionMemberCapacity");
        RequirePositive(RouteStateCapacity, "routeStateCapacity");
        RequirePositive(RouteMaxExpandedPerRequest, "routeMaxExpandedPerRequest");
        RequirePositive(RouteWaypointCapacityPerAgent, "routeWaypointCapacityPerAgent");
        RequirePositive(LoadedChunkCapacity, "loadedChunkCapacity");
        RequirePositive(RelationshipDomainCapacity, "relationshipDomainCapacity");
        RequirePositive(DisplacedAgentCapacity, "displacedAgentCapacity");

        if (MovePlanExecutionGroupCapacity < NavigationGroupCapacity)
        {
            throw new InvalidOperationException(
                "MassNavigation scenarioRuntime.runtimeCapacity.movePlanExecutionGroupCapacity must be >= scenarioRuntime.runtimeCapacity.navigationGroupCapacity.");
        }

    }

    public void ValidateForScenario(int teamCount, int agentsPerTeam)
    {
        if (teamCount <= 0)
        {
            throw new InvalidOperationException("MassNavigation runtimeCapacity scenario validation requires a positive team count.");
        }

        long authoredAgentCount = (long)teamCount * agentsPerTeam;
        if (authoredAgentCount > GroupMembershipAgentCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation scenarioRuntime.runtimeCapacity.groupMembershipAgentCapacity {GroupMembershipAgentCapacity} is smaller than authored scenario agent count {authoredAgentCount}.");
        }

        if (authoredAgentCount > GroupMemberCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation scenarioRuntime.runtimeCapacity.groupMemberCapacity {GroupMemberCapacity} is smaller than authored scenario agent count {authoredAgentCount}.");
        }

        if (authoredAgentCount > MovePlanExecutionMemberCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation scenarioRuntime.runtimeCapacity.movePlanExecutionMemberCapacity {MovePlanExecutionMemberCapacity} is smaller than authored scenario agent count {authoredAgentCount}.");
        }

        if (teamCount > RelationshipDomainCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation scenarioRuntime.runtimeCapacity.relationshipDomainCapacity {RelationshipDomainCapacity} is smaller than authored scenario team count {teamCount}.");
        }
    }

    public void ValidateForStreaming(MassNavigationWorldConfig world, MassNavigationStreamingConfig streaming)
    {
        if (world == null)
        {
            throw new InvalidOperationException("MassNavigation runtimeCapacity streaming validation requires world config.");
        }

        if (streaming == null)
        {
            throw new InvalidOperationException("MassNavigation runtimeCapacity streaming validation requires streaming config.");
        }

        int minimumWindowChunkCapacity = CountSquareChunksForRadius(
            streaming.RadiusCm,
            world.StreamingChunkSizeCm);
        if (LoadedChunkCapacity < minimumWindowChunkCapacity)
        {
            throw new InvalidOperationException(
                $"MassNavigation scenarioRuntime.runtimeCapacity.loadedChunkCapacity {LoadedChunkCapacity} is smaller than one streaming window chunk count {minimumWindowChunkCapacity}.");
        }
    }

    private static void RequirePositive(int value, string fieldName)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"MassNavigation scenarioRuntime.runtimeCapacity.{fieldName} must be > 0.");
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

    private readonly ConfigPipeline _pipeline;

    public MassNavigationConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public MassNavigationConfig Load(
        ConfigCatalog catalog,
        ConfigConflictReport report,
        string relativePath = DefaultRelativePath)
    {
        if (TryLoad(catalog, report, out MassNavigationConfig? config, relativePath))
        {
            return config;
        }

        throw new InvalidOperationException($"MassNavigation runtime config '{relativePath}' must be registered in config_catalog.json.");
    }

    public bool TryLoad(
        ConfigCatalog catalog,
        ConfigConflictReport report,
        [NotNullWhen(true)] out MassNavigationConfig? config,
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

        if (!catalog.TryGet(relativePath, out ConfigCatalogEntry entry))
        {
            return false;
        }

        if (entry.MergePolicy != ConfigMergePolicy.DeepObject)
        {
            throw new InvalidOperationException($"MassNavigation runtime config '{relativePath}' must use DeepObject merge policy.");
        }

        JsonObject? merged = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
        if (merged == null)
        {
            throw new InvalidOperationException($"MassNavigation runtime requires config '{relativePath}' through ConfigPipeline.");
        }

        config = MassNavigationConfig.Load(merged);
        return true;
    }
}

public sealed class MassNavigationStreamingConfig
{
    public float RetainSeconds { get; set; }
    public int RadiusCm { get; set; }

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
    public string[] RequiredMeshAssetIds { get; set; } = Array.Empty<string>();
    public string BlockerPerformerId { get; set; } = string.Empty;
    public string HotspotPerformerId { get; set; } = string.Empty;
    public string BlockerTemplateId { get; set; } = string.Empty;
    public string HotspotTemplateId { get; set; } = string.Empty;
    public MassNavigationTeamPresentationConfig[] Teams { get; set; } = Array.Empty<MassNavigationTeamPresentationConfig>();

    public void Validate(
        MassNavigationScenarioConfig scenario,
        MassNavigationScenarioRuntimeConfig scenarioRuntime,
        MassNavigationWorldConfig? world)
    {
        if (scenarioRuntime == null)
        {
            throw new InvalidOperationException("MassNavigation presentation requires an explicit scenarioRuntime section.");
        }

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

        if (!scenarioRuntime.AutoSpawnConfiguredScenario)
        {
            if (Teams.Length != 0)
            {
                throw new InvalidOperationException(
                    "MassNavigation presentation.teams must be empty when scenarioRuntime.autoSpawnConfiguredScenario is false; externally-authored scenarios must author agent templates in their own config.");
            }

            return;
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

    private static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"MassNavigation presentation requires non-empty {fieldName}.");
        }
    }
}

public sealed class MassNavigationFlowSolverConfig
{
    public int FieldWidthCm { get; set; }
    public int FieldHeightCm { get; set; }
    public int FlowCellSizeCm { get; set; }
    public int MaxObstacleCount { get; set; }
    public int ParallelWorkerCount { get; set; }
    public int SeparationHashCellSizeCm { get; set; }
    public int SeparationHashMinSearchRadiusCells { get; set; }
    public int HardResolveHashCellSizeCm { get; set; }
    public int HardResolveHashMinSearchRadiusCells { get; set; }
    public float PlayAreaMinXCm { get; set; }
    public float PlayAreaMaxXCm { get; set; }
    public float PlayAreaMinYCm { get; set; }
    public float PlayAreaMaxYCm { get; set; }

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
    public int TeamId { get; set; }
    public string StyleId { get; set; } = string.Empty;
    public string LightTemplateId { get; set; } = string.Empty;
    public string HeavyTemplateId { get; set; } = string.Empty;
    public string LightPerformerId { get; set; } = string.Empty;
    public string HeavyPerformerId { get; set; } = string.Empty;

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
    public int SolverWindowWidthCm { get; set; }
    public int SolverWindowHeightCm { get; set; }
    public int StreamingChunkSizeCm { get; set; }
    public int CommandFocusHoldTicks { get; set; }
    public int WorkAreaPaddingCm { get; set; }
    public int WorkAreaMaxWidthCm { get; set; }
    public int WorkAreaMaxHeightCm { get; set; }
    public string ActiveHotZoneId { get; set; } = string.Empty;
    public MassNavigationHotZoneConfig[] HotZones { get; set; } = Array.Empty<MassNavigationHotZoneConfig>();

    public MassNavigationHotZoneConfig GetRequiredHotZone(string hotZoneId)
    {
        if (!TryGetHotZone(hotZoneId, out MassNavigationHotZoneConfig hotZone))
        {
            throw new InvalidOperationException($"MassNavigation world hot zone '{hotZoneId}' is not configured.");
        }

        return hotZone;
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

        if (SolverWindowWidthCm != solver.FieldWidthCm ||
            SolverWindowHeightCm != solver.FieldHeightCm)
        {
            throw new InvalidOperationException(
                $"MassNavigation world solver window must match solver field size ({solver.FieldWidthCm}x{solver.FieldHeightCm} cm).");
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

        if (WorkAreaMaxWidthCm < SolverWindowWidthCm || WorkAreaMaxHeightCm < SolverWindowHeightCm)
        {
            throw new InvalidOperationException("MassNavigation world work area max must be at least the solver cache size.");
        }

        if (string.IsNullOrWhiteSpace(ActiveHotZoneId))
        {
            throw new InvalidOperationException("MassNavigation world requires ActiveHotZoneId as the initial hotspot debug landmark.");
        }

        GetRequiredHotZone(ActiveHotZoneId);
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
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int CenterXCm { get; set; }
    public int CenterYCm { get; set; }
    public int WidthCm { get; set; }
    public int HeightCm { get; set; }

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

        if (WidthCm <= 0 || HeightCm <= 0)
        {
            throw new InvalidOperationException($"MassNavigation hot zone '{Id}' requires positive width and height.");
        }
    }
}

public sealed class MassNavigationScenarioConfig
{
    public int AgentsPerTeam { get; set; }
    public MassNavigationScenarioTeamConfig[] Teams { get; set; } = Array.Empty<MassNavigationScenarioTeamConfig>();
    public MassNavigationScenarioSpawnLayoutConfig SpawnLayout { get; set; } = new();

    public void Validate(MassNavigationScenarioRuntimeConfig scenarioRuntime)
    {
        if (scenarioRuntime == null)
        {
            throw new InvalidOperationException("MassNavigation scenario validation requires an explicit scenarioRuntime section.");
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

        scenarioRuntime.RuntimeCapacity.ValidateForScenario(Teams.Length, AgentsPerTeam);

        if (scenarioRuntime.AutoSpawnConfiguredScenario)
        {
            SpawnLayout.Validate();
        }
    }
}

public enum MassNavigationScenarioSpawnLayoutKind : byte
{
    OrbitOpposedTargets = 1,
}

public sealed class MassNavigationScenarioSpawnLayoutConfig
{
    private MassNavigationScenarioSpawnLayoutKind _parsedKind;

    public string Kind { get; set; } = string.Empty;
    public float OrbitRadiusCm { get; set; }
    public int RandomSeed { get; set; }

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
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
