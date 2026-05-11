using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Teams;

namespace MassNavigationMod.Runtime;

public sealed class MassNavigationConfig
{
    public string MapId { get; set; } = string.Empty;
    public MassNavigationWorldConfig? World { get; set; }
    public MassNavigationPresentationConfig Presentation { get; set; } = new();
    public MassNavigationScenarioConfig Scenario { get; set; } = new();
    public MassNavigationScenarioRuntimeConfig ScenarioRuntime { get; set; } = new();
    public MassNavigationCadenceConfig Cadence { get; set; } = new();
    public MassNavigationAgentProfileSetConfig AgentProfiles { get; set; } = new();
    public MassNavigationCameraProfilesConfig CameraProfiles { get; set; } = new();
    public TeamConfig TeamRelationships { get; set; } = new();
    public MassFlowTuning Flow { get; set; } = new();
    public MassFlowArrivalTuning Arrival { get; set; } = new();
    public MassFlowAvoidanceTuning Avoidance { get; set; } = new();
    public MassNavigationCrowdSemantics Semantics { get; set; } = new();
    public MassNavigationViewResidencyConfig ViewResidency { get; set; } = new();

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
        RequireProperty(root, "presentation");
        RequireProperty(root, "scenario");
        RequireProperty(root, "scenarioRuntime");
        RequireProperty(root, "cadence");
        RequireProperty(root, "agentProfiles");
        RequireProperty(root, "cameraProfiles");
        RequireProperty(root, "teamRelationships");
        RequireProperty(root, "flow");
        RequireProperty(root, "arrival");
        RequireProperty(root, "avoidance");
        RequireProperty(root, "semantics");
        RequireProperty(root, "viewResidency");

        JsonElement world = RequireProperty(root, "world");
        RequireProperties(
            world,
            "solverWindowWidthCm",
            "solverWindowHeightCm",
            "streamingChunkSizeCm",
            "streamingRadiusCm",
            "cameraFocusShiftThresholdCm",
            "commandFocusHoldTicks",
            "workAreaPaddingCm",
            "workAreaMaxWidthCm",
            "workAreaMaxHeightCm",
            "activeHotZoneId",
            "hotZones",
            "obstacles");
        JsonElement presentation = RequireProperty(root, "presentation");
        RequireProperties(
            presentation,
            "requiredMeshAssetIds",
            "blockerPerformerId",
            "hotspotPerformerId",
            "blockerTemplateId",
            "hotspotTemplateId",
            "teams");
        JsonElement scenario = RequireProperty(root, "scenario");
        RequireProperties(
            scenario,
            "agentsPerTeam",
            "initialSelectedTeamId",
            "teams");
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
            throw new InvalidOperationException("Mass-nav agentProfiles.profiles must be an explicit array.");
        }

        int profileIndex = 0;
        foreach (JsonElement profile in profiles.EnumerateArray())
        {
            RequireProperties(
                profile,
                "id",
                "heavy",
                "navMass",
                "visualScale",
                "everyNth",
                "nthOffset");
            profileIndex++;
        }

        if (profileIndex <= 0)
        {
            throw new InvalidOperationException("Mass-nav agentProfiles.profiles requires at least one explicit profile.");
        }

        JsonElement relationships = RequireProperty(root, "teamRelationships");
        RequireProperty(relationships, "defaultRelationship");
        RequireProperty(relationships, "relationships");
        RequireProperties(RequireProperty(root, "cameraProfiles"), "tacticalProfileId", "strategicProfileId");
        RequireProperties(RequireProperty(root, "scenarioRuntime"), "autoSpawnConfiguredScenario");
        RequireProperties(
            RequireProperty(root, "flow"),
            "enabled",
            "iterationsPerStep",
            "stepIntervalTicks",
            "crowdStampIntervalTicks",
            "obstacleStampIntervalTicks",
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
            "lightNavMass",
            "heavyNavMass",
            "lightVisualScale",
            "heavyVisualScale",
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
        JsonElement semantics = RequireProperty(root, "semantics");
        RequireProperties(
            RequireProperty(semantics, "obstacle"),
            "agentBodyRadiusCm",
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
            "formationLineSpacingCm",
            "formationSquareSpacingCm",
            "formationCircleSpacingCm",
            "formationCircleMinRadiusCm",
            "formationWedgeSpacingCm",
            "formationRotationEpsilonRadians",
            "formationRotationSpeedRadiansPerSecond",
            "pullDeadZoneCm",
            "pullClampCm",
            "arrivedRadiusCm",
            "formationArriveThresholdCm",
            "looseArriveThresholdCm",
            "unitTargetStopThresholdCm",
            "formationFlowSlowRadiusCm",
            "nearSlotBlend",
            "farSlotBlend",
            "nearSlotBlendDistanceSq");
        RequireProperties(
            RequireProperty(semantics, "steering"),
            "speedCmPerSecond",
            "separationRadiusCm",
            "goalArrivalRadiusCm",
            "flowObstacleAvoidanceScale",
            "formationSeparationScale",
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
            "coincidentPairHashBucketCount",
            "coincidentPairHashPrimeA",
            "coincidentPairHashPrimeB");
        JsonElement viewResidency = RequireProperty(root, "viewResidency");
        RequireProperties(
            viewResidency,
            "mode",
            "retainSeconds",
            "radiusCm",
            "initialProbeId",
            "cameraProbes");
        JsonElement cameraProbes = RequireProperty(viewResidency, "cameraProbes");
        if (cameraProbes.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Mass-nav viewResidency.cameraProbes must be an explicit array.");
        }

        int probeIndex = 0;
        foreach (JsonElement probe in cameraProbes.EnumerateArray())
        {
            RequireProperties(
                probe,
                "id",
                "label",
                "targetXCm",
                "targetYCm",
                "distanceCm",
                "yaw",
                "pitch",
                "fovYDeg");
            probeIndex++;
        }

        if (probeIndex <= 0)
        {
            throw new InvalidOperationException("Mass-nav viewResidency.cameraProbes requires at least one explicit probe.");
        }
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"Mass-nav config requires explicit '{propertyName}' property.");
        }

        return value;
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
            throw new InvalidOperationException("Mass-nav config requires a non-empty map id.");
        }

        Scenario.Validate();
        ScenarioRuntime.Validate();
        Presentation.Validate(Scenario);
        Cadence.Validate();
        AgentProfiles.Validate();
        CameraProfiles.Validate();
        ViewResidency.Validate();
        Flow.Validate();
        Arrival.Validate();
        Avoidance.Validate();
        Semantics.Validate();
        if (World == null)
        {
            throw new InvalidOperationException("Mass-nav config requires an explicit world section.");
        }

        World.Validate();

        ValidateRelationships();

        var knownTeams = new HashSet<int>(Scenario.Teams.Select(team => team.Id));
        for (int i = 0; i < TeamRelationships.Relationships.Count; i++)
        {
            RelationshipEntry relation = TeamRelationships.Relationships[i];
            if (!knownTeams.Contains(relation.TeamA) || !knownTeams.Contains(relation.TeamB))
            {
                throw new InvalidOperationException(
                    $"Mass-nav config relationship [{relation.TeamA},{relation.TeamB}] references an unknown team.");
            }

            if (!TeamManager.TryParseRelationship(relation.Attitude, out _))
            {
                throw new InvalidOperationException(
                    $"Mass-nav config relationship [{relation.TeamA},{relation.TeamB}] has invalid attitude '{relation.Attitude}'.");
            }
        }
    }

    private void ValidateRelationships()
    {
        if (TeamRelationships == null)
        {
            throw new InvalidOperationException("Mass-nav config requires an explicit teamRelationships section.");
        }

        if (string.IsNullOrWhiteSpace(TeamRelationships.DefaultRelationship) ||
            !TeamManager.TryParseRelationship(TeamRelationships.DefaultRelationship, out _))
        {
            throw new InvalidOperationException(
                $"Mass-nav config teamRelationships.defaultRelationship is invalid: '{TeamRelationships.DefaultRelationship}'.");
        }

        if (TeamRelationships.Relationships == null)
        {
            throw new InvalidOperationException("Mass-nav config requires teamRelationships.relationships as an explicit array.");
        }
    }
}

public sealed class MassNavigationScenarioRuntimeConfig
{
    public bool AutoSpawnConfiguredScenario { get; set; }

    public void Validate()
    {
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
            throw new InvalidOperationException($"MassNavigationMod config '{relativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"MassNavigationMod config '{relativePath}' must use Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"MassNavigationMod requires config '{relativePath}' through ConfigPipeline.");
        }

        return MassNavigationConfig.Load(merged);
    }
}

public sealed class MassNavigationCameraProfilesConfig
{
    public string TacticalProfileId { get; set; } = string.Empty;
    public string StrategicProfileId { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TacticalProfileId))
        {
            throw new InvalidOperationException("Mass-nav cameraProfiles requires non-empty TacticalProfileId.");
        }

        if (string.IsNullOrWhiteSpace(StrategicProfileId))
        {
            throw new InvalidOperationException("Mass-nav cameraProfiles requires non-empty StrategicProfileId.");
        }

        if (string.Equals(TacticalProfileId, StrategicProfileId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Mass-nav cameraProfiles tactical and strategic profile ids must be distinct.");
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

    public void Validate(MassNavigationScenarioConfig scenario)
    {
        if (RequiredMeshAssetIds.Length <= 0)
        {
            throw new InvalidOperationException("Mass-nav presentation requires at least one RequiredMeshAssetIds entry.");
        }

        var meshIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < RequiredMeshAssetIds.Length; i++)
        {
            string meshAssetId = RequiredMeshAssetIds[i];
            RequireNonEmpty(meshAssetId, $"{nameof(RequiredMeshAssetIds)}[{i}]");
            if (!meshIds.Add(meshAssetId))
            {
                throw new InvalidOperationException($"Mass-nav presentation contains duplicate required mesh asset '{meshAssetId}'.");
            }
        }

        RequireNonEmpty(BlockerPerformerId, nameof(BlockerPerformerId));
        RequireNonEmpty(HotspotPerformerId, nameof(HotspotPerformerId));
        RequireNonEmpty(BlockerTemplateId, nameof(BlockerTemplateId));
        RequireNonEmpty(HotspotTemplateId, nameof(HotspotTemplateId));

        if (Teams.Length != scenario.Teams.Length)
        {
            throw new InvalidOperationException("Mass-nav presentation team style count must match scenario teams.");
        }

        var scenarioTeamIds = new HashSet<int>(scenario.Teams.Select(team => team.Id));
        var seenIds = new HashSet<int>();
        for (int i = 0; i < Teams.Length; i++)
        {
            MassNavigationTeamPresentationConfig team = Teams[i];
            team.Validate();
            if (!scenarioTeamIds.Contains(team.TeamId))
            {
                throw new InvalidOperationException($"Mass-nav presentation team style references unknown team {team.TeamId}.");
            }

            if (!seenIds.Add(team.TeamId))
            {
                throw new InvalidOperationException($"Mass-nav presentation contains duplicate style for team {team.TeamId}.");
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

        throw new InvalidOperationException($"Mass-nav presentation missing team style for team {teamId}.");
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
            throw new InvalidOperationException($"Mass-nav presentation requires non-empty {fieldName}.");
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
            throw new InvalidOperationException("Mass-nav presentation team style requires TeamId > 0.");
        }

        if (string.IsNullOrWhiteSpace(StyleId))
        {
            throw new InvalidOperationException($"Mass-nav presentation team {TeamId} requires StyleId.");
        }

        if (string.IsNullOrWhiteSpace(LightTemplateId))
        {
            throw new InvalidOperationException($"Mass-nav presentation team {TeamId} requires LightTemplateId.");
        }

        if (string.IsNullOrWhiteSpace(HeavyTemplateId))
        {
            throw new InvalidOperationException($"Mass-nav presentation team {TeamId} requires HeavyTemplateId.");
        }

        if (string.IsNullOrWhiteSpace(LightPerformerId))
        {
            throw new InvalidOperationException($"Mass-nav presentation team {TeamId} requires LightPerformerId.");
        }

        if (string.IsNullOrWhiteSpace(HeavyPerformerId))
        {
            throw new InvalidOperationException($"Mass-nav presentation team {TeamId} requires HeavyPerformerId.");
        }
    }
}

public sealed class MassNavigationWorldConfig
{
    private int _activeHotZoneIndex = -1;

    public int SolverWindowWidthCm { get; set; }
    public int SolverWindowHeightCm { get; set; }
    public int StreamingChunkSizeCm { get; set; }
    public int StreamingRadiusCm { get; set; }
    public int CameraFocusShiftThresholdCm { get; set; }
    public int CommandFocusHoldTicks { get; set; }
    public int WorkAreaPaddingCm { get; set; }
    public int WorkAreaMaxWidthCm { get; set; }
    public int WorkAreaMaxHeightCm { get; set; }
    public string ActiveHotZoneId { get; set; } = string.Empty;
    public MassNavigationHotZoneConfig[] HotZones { get; set; } = Array.Empty<MassNavigationHotZoneConfig>();
    public MassNavigationObstacleConfig[] Obstacles { get; set; } = Array.Empty<MassNavigationObstacleConfig>();

    [JsonIgnore]
    public MassNavigationHotZoneConfig ActiveHotZone => _activeHotZoneIndex >= 0 && _activeHotZoneIndex < HotZones.Length
        ? HotZones[_activeHotZoneIndex]
        : throw new InvalidOperationException("Mass-nav world active hot zone was not validated.");

    [JsonIgnore]
    public float HotZoneMinXCm => ActiveHotZone.CenterXCm - (ActiveHotZone.WidthCm * 0.5f);

    [JsonIgnore]
    public float HotZoneMinYCm => ActiveHotZone.CenterYCm - (ActiveHotZone.HeightCm * 0.5f);

    [JsonIgnore]
    public float HotZoneMaxXCm => ActiveHotZone.CenterXCm + (ActiveHotZone.WidthCm * 0.5f);

    [JsonIgnore]
    public float HotZoneMaxYCm => ActiveHotZone.CenterYCm + (ActiveHotZone.HeightCm * 0.5f);

    [JsonIgnore]
    public int HotZoneWidthCm => ActiveHotZone.WidthCm;

    [JsonIgnore]
    public int HotZoneHeightCm => ActiveHotZone.HeightCm;

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
            throw new InvalidOperationException($"Mass-nav world hot zone '{hotZoneId}' is not configured.");
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

    public void Validate()
    {
        if (HotZones.Length <= 0)
        {
            throw new InvalidOperationException("Mass-nav world requires at least one configured hotspot debug landmark.");
        }

        if (Obstacles.Length <= 0)
        {
            throw new InvalidOperationException("Mass-nav world requires explicitly authored obstacles.");
        }

        if (Obstacles.Length > MassFlowSimulationState.MaxObstacleCount)
        {
            throw new InvalidOperationException(
                $"Mass-nav world obstacle count {Obstacles.Length} exceeds solver capacity {MassFlowSimulationState.MaxObstacleCount}.");
        }

        if (SolverWindowWidthCm != MassFlowSimulationState.FieldWidthCm ||
            SolverWindowHeightCm != MassFlowSimulationState.FieldHeightCm)
        {
            throw new InvalidOperationException(
                $"Mass-nav world solver window must be explicit and match the current SoA solver cache ({MassFlowSimulationState.FieldWidthCm}x{MassFlowSimulationState.FieldHeightCm} cm).");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < HotZones.Length; i++)
        {
            MassNavigationHotZoneConfig zone = HotZones[i];
            zone.Validate();
            if (!ids.Add(zone.Id))
            {
                throw new InvalidOperationException($"Mass-nav world contains duplicate hot zone id '{zone.Id}'.");
            }

        }

        var obstacleIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Obstacles.Length; i++)
        {
            MassNavigationObstacleConfig obstacle = Obstacles[i];
            obstacle.Validate();
            if (!obstacleIds.Add(obstacle.Id))
            {
                throw new InvalidOperationException($"Mass-nav world contains duplicate obstacle id '{obstacle.Id}'.");
            }

            float minX = obstacle.LocalXCm - obstacle.RadiusCm;
            float maxX = obstacle.LocalXCm + obstacle.RadiusCm;
            float minY = obstacle.LocalYCm - obstacle.RadiusCm;
            float maxY = obstacle.LocalYCm + obstacle.RadiusCm;
            if (minX < 0f || maxX > SolverWindowWidthCm ||
                minY < 0f || maxY > SolverWindowHeightCm)
            {
                throw new InvalidOperationException(
                    $"Mass-nav obstacle '{obstacle.Id}' must fit inside the authored solver window.");
            }
        }

        if (StreamingChunkSizeCm <= 0)
        {
            throw new InvalidOperationException("Mass-nav world requires StreamingChunkSizeCm > 0.");
        }

        if (StreamingRadiusCm < 0)
        {
            throw new InvalidOperationException("Mass-nav world requires StreamingRadiusCm >= 0.");
        }

        if (CameraFocusShiftThresholdCm <= 0)
        {
            throw new InvalidOperationException("Mass-nav world requires CameraFocusShiftThresholdCm > 0.");
        }

        if (CommandFocusHoldTicks < 0)
        {
            throw new InvalidOperationException("Mass-nav world requires CommandFocusHoldTicks >= 0.");
        }

        if (WorkAreaPaddingCm < 0)
        {
            throw new InvalidOperationException("Mass-nav world requires WorkAreaPaddingCm >= 0.");
        }

        if (WorkAreaMaxWidthCm <= 0 || WorkAreaMaxHeightCm <= 0)
        {
            throw new InvalidOperationException("Mass-nav world requires positive WorkAreaMaxWidthCm and WorkAreaMaxHeightCm.");
        }

        if (WorkAreaMaxWidthCm < SolverWindowWidthCm || WorkAreaMaxHeightCm < SolverWindowHeightCm)
        {
            throw new InvalidOperationException("Mass-nav world work area max must be at least the solver cache size.");
        }

        if (string.IsNullOrWhiteSpace(ActiveHotZoneId))
        {
            throw new InvalidOperationException("Mass-nav world requires ActiveHotZoneId as the initial hotspot debug landmark.");
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

public sealed class MassNavigationViewResidencyConfig
{
    private int _activeProbeIndex = -1;

    public string Mode { get; set; } = "Camera";
    public float RetainSeconds { get; set; }
    public int RadiusCm { get; set; }
    public string InitialProbeId { get; set; } = string.Empty;
    public MassNavigationCameraProbeConfig[] CameraProbes { get; set; } = Array.Empty<MassNavigationCameraProbeConfig>();

    [JsonIgnore]
    public bool UsesProbeFocus => string.Equals(Mode, "Probe", StringComparison.Ordinal);

    [JsonIgnore]
    public MassNavigationCameraProbeConfig ActiveProbe => _activeProbeIndex >= 0 && _activeProbeIndex < CameraProbes.Length
        ? CameraProbes[_activeProbeIndex]
        : throw new InvalidOperationException("Mass-nav view residency active probe was not validated.");

    public void SetActiveProbe(string probeId)
    {
        int index = FindProbeIndex(probeId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Mass-nav view residency probe '{probeId}' is not configured.");
        }

        _activeProbeIndex = index;
        InitialProbeId = CameraProbes[index].Id;
    }

    public bool TryGetProbe(string probeId, out MassNavigationCameraProbeConfig probe)
    {
        int index = FindProbeIndex(probeId);
        if (index < 0)
        {
            probe = null!;
            return false;
        }

        probe = CameraProbes[index];
        return true;
    }

    public void Validate()
    {
        if (!string.Equals(Mode, "Camera", StringComparison.Ordinal) &&
            !string.Equals(Mode, "Probe", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Mass-nav viewResidency.mode must be 'Camera' or 'Probe', got '{Mode}'.");
        }

        if (RetainSeconds < 0f)
        {
            throw new InvalidOperationException("Mass-nav viewResidency.retainSeconds must be >= 0.");
        }

        if (RadiusCm <= 0)
        {
            throw new InvalidOperationException("Mass-nav viewResidency.radiusCm must be > 0.");
        }

        if (CameraProbes.Length <= 0)
        {
            throw new InvalidOperationException("Mass-nav viewResidency.cameraProbes requires at least one explicit probe.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < CameraProbes.Length; i++)
        {
            MassNavigationCameraProbeConfig probe = CameraProbes[i];
            probe.Validate();
            if (!ids.Add(probe.Id))
            {
                throw new InvalidOperationException($"Mass-nav viewResidency contains duplicate camera probe id '{probe.Id}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(InitialProbeId))
        {
            throw new InvalidOperationException("Mass-nav viewResidency.initialProbeId must be explicit.");
        }

        SetActiveProbe(InitialProbeId);
    }

    private int FindProbeIndex(string probeId)
    {
        for (int i = 0; i < CameraProbes.Length; i++)
        {
            if (string.Equals(CameraProbes[i].Id, probeId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}

public sealed class MassNavigationCameraProbeConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public float TargetXCm { get; set; }
    public float TargetYCm { get; set; }
    public float DistanceCm { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float FovYDeg { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException("Mass-nav camera probe requires a non-empty id.");
        }

        if (string.IsNullOrWhiteSpace(Label))
        {
            throw new InvalidOperationException($"Mass-nav camera probe '{Id}' requires a non-empty label.");
        }

        if (DistanceCm <= 0f)
        {
            throw new InvalidOperationException($"Mass-nav camera probe '{Id}' requires DistanceCm > 0.");
        }

        if (FovYDeg <= 0f || FovYDeg >= 180f)
        {
            throw new InvalidOperationException($"Mass-nav camera probe '{Id}' requires 0 < FovYDeg < 180.");
        }
    }
}

public sealed class MassNavigationObstacleConfig
{
    public string Id { get; set; } = string.Empty;
    public float LocalXCm { get; set; }
    public float LocalYCm { get; set; }
    public float RadiusCm { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException("Mass-nav obstacle requires a non-empty id.");
        }

        if (RadiusCm <= 0f)
        {
            throw new InvalidOperationException($"Mass-nav obstacle '{Id}' requires RadiusCm > 0.");
        }
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
            throw new InvalidOperationException("Mass-nav hot zone requires a non-empty id.");
        }

        if (string.IsNullOrWhiteSpace(Label))
        {
            throw new InvalidOperationException($"Mass-nav hot zone '{Id}' requires a non-empty label.");
        }

        if (WidthCm <= 0 || HeightCm <= 0)
        {
            throw new InvalidOperationException($"Mass-nav hot zone '{Id}' requires positive width and height.");
        }
    }
}

public sealed class MassNavigationScenarioConfig
{
    public int AgentsPerTeam { get; set; }
    public int InitialSelectedTeamId { get; set; }
    public MassNavigationScenarioTeamConfig[] Teams { get; set; } = Array.Empty<MassNavigationScenarioTeamConfig>();

    public void Validate()
    {
        if (AgentsPerTeam < 0)
        {
            throw new InvalidOperationException("Mass-nav config requires AgentsPerTeam >= 0.");
        }

        if (Teams.Length <= 0)
        {
            throw new InvalidOperationException("Mass-nav config requires at least one team.");
        }

        var seenIds = new HashSet<int>();
        for (int i = 0; i < Teams.Length; i++)
        {
            MassNavigationScenarioTeamConfig team = Teams[i];
            if (team.Id <= 0)
            {
                throw new InvalidOperationException("Mass-nav config team ids must be positive.");
            }

            if (string.IsNullOrWhiteSpace(team.Name))
            {
                throw new InvalidOperationException($"Mass-nav config team {team.Id} requires a name.");
            }

            if (!seenIds.Add(team.Id))
            {
                throw new InvalidOperationException($"Mass-nav config contains duplicate team id {team.Id}.");
            }
        }

        if (!seenIds.Contains(InitialSelectedTeamId))
        {
            throw new InvalidOperationException(
                $"Mass-nav config InitialSelectedTeamId {InitialSelectedTeamId} is not present in Scenario.Teams.");
        }
    }
}

public sealed class MassNavigationScenarioTeamConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
