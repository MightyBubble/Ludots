using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Presentation.Minimap;

namespace Ludots.Core.MassCrowd.Runtime;

public sealed class MassNavigationConfig
{
    public string MapId { get; set; } = string.Empty;
    public MassNavigationWorldConfig? World { get; set; }
    public MassFlowSolverConfig Solver { get; set; } = new();
    public MassNavigationPresentationConfig Presentation { get; set; } = new();
    public MassNavigationScenarioConfig Scenario { get; set; } = new();
    public MassNavigationScenarioRuntimeConfig ScenarioRuntime { get; set; } = new();
    public MassNavigationCadenceConfig Cadence { get; set; } = new();
    public MassNavigationAgentProfileSetConfig AgentProfiles { get; set; } = new();
    public MassNavigationCameraProfilesConfig CameraProfiles { get; set; } = new();
    public MassNavigationMinimapConfig Minimap { get; set; } = new();
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
        RequireProperty(root, "solver");
        RequireProperty(root, "presentation");
        RequireProperty(root, "scenario");
        RequireProperty(root, "scenarioRuntime");
        RequireProperty(root, "cadence");
        RequireProperty(root, "agentProfiles");
        RequireProperty(root, "cameraProfiles");
        RequireProperty(root, "minimap");
        RequireProperty(root, "teamRelationships");
        RequireProperty(root, "flow");
        RequireProperty(root, "arrival");
        RequireProperty(root, "avoidance");
        RequireProperty(root, "semantics");
        RequireProperty(root, "viewResidency");
        JsonElement scenarioRuntime = RequireProperty(root, "scenarioRuntime");
        bool autoSpawnConfiguredScenario = RequireBooleanProperty(scenarioRuntime, "autoSpawnConfiguredScenario");
        RequireProperties(
            scenarioRuntime,
            "initialSelectionScratchCapacity",
            "initialSelectedEntityCapacity",
            "runtimeCapacity",
            "panel",
            "panelControls");
        RequireProperties(
            RequireProperty(scenarioRuntime, "panel"),
            "mode");
        RequireProperties(
            RequireProperty(scenarioRuntime, "runtimeCapacity"),
            "navigationGroupCapacity",
            "groupMembershipAgentCapacity",
            "selectionMemberScratchCapacity",
            "groupMemberCapacity",
            "orderIngestionTokenCapacity",
            "orderIngestionMemberCapacity",
            "loadedChunkCapacity",
            "metadataTeamCapacity");
        RequireProperties(
            RequireProperty(scenarioRuntime, "panelControls"),
            "maxAgentsPerTeam",
            "totalAgentStep",
            "totalAgentPresets",
            "panelRefreshIntervalSeconds",
            "viewResidencyRetainSecondsStep",
            "simulationBudgetStepMs",
            "simulationBudgetMinMs",
            "simulationBudgetMaxMs",
            "simulationSliceStep",
            "simulationSliceMin",
            "simulationSliceMax",
            "enginePolicyHzStep",
            "enginePolicyHzMin",
            "enginePolicyHzMax",
            "enginePolicyMaxStepsStep",
            "enginePolicyMaxStepsMin",
            "enginePolicyMaxStepsMax",
            "arrivalTimeoutStepMs",
            "arrivalProgressStepCm",
            "arrivalWakePushStepCm",
            "arrivalRetryStep",
            "flowIterationStep",
            "flowCadenceHzStep");

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
            "initialSelectedTeamId",
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
            "hardResolveCandidateThresholdAgents",
            "orderIdleScanIntervalFrames");
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
                "visualScale",
                "speedCmPerSecond",
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
        JsonElement cameraProfiles = RequireProperty(root, "cameraProfiles");
        RequireProperties(cameraProfiles, "tacticalProfileId", "strategicProfileId", "requestPolicy");
        RequireProperties(
            RequireProperty(cameraProfiles, "requestPolicy"),
            "blendDurationSeconds",
            "resetRuntimeState",
            "snapToFollowTargetWhenAvailable",
            "strategicTargetXCm",
            "strategicTargetYCm");
        RequireProperties(
            RequireProperty(root, "minimap"),
            "visible",
            "initialPreset",
            "followCameraHalfExtentCm",
            "rotateWithCamera");
        RequireProperties(
            RequireProperty(root, "flow"),
            "enabled",
            "iterationsPerStep",
            "maxIterationsPerStep",
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

    private static bool RequireBooleanProperty(JsonElement root, string propertyName)
    {
        JsonElement value = RequireProperty(root, propertyName);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException($"Mass-nav config requires explicit boolean '{propertyName}' property.")
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
            throw new InvalidOperationException("Mass-nav config requires a non-empty map id.");
        }

        Solver.Validate();
        ScenarioRuntime.Validate();
        Scenario.Validate(ScenarioRuntime);
        Presentation.Validate(Scenario, ScenarioRuntime, World);
        Cadence.Validate();
        AgentProfiles.Validate();
        CameraProfiles.Validate();
        Minimap.Validate();
        ViewResidency.Validate();
        Flow.Validate();
        Arrival.Validate();
        Avoidance.Validate();
        Semantics.Validate();
        if (World == null)
        {
            throw new InvalidOperationException("Mass-nav config requires an explicit world section.");
        }

        World.Validate(Solver);
        ScenarioRuntime.RuntimeCapacity.ValidateForStreaming(World, ViewResidency);

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
    public int InitialSelectionScratchCapacity { get; set; }
    public int InitialSelectedEntityCapacity { get; set; }
    public MassNavigationRuntimeCapacityConfig RuntimeCapacity { get; set; } = new();
    public MassNavigationPanelConfig Panel { get; set; } = new();
    public MassNavigationPanelControlsConfig PanelControls { get; set; } = new();

    public void Validate()
    {
        if (InitialSelectionScratchCapacity <= 0)
        {
            throw new InvalidOperationException("Mass-nav scenarioRuntime.initialSelectionScratchCapacity must be > 0.");
        }

        if (InitialSelectedEntityCapacity <= 0)
        {
            throw new InvalidOperationException("Mass-nav scenarioRuntime.initialSelectedEntityCapacity must be > 0.");
        }

        if (RuntimeCapacity == null)
        {
            throw new InvalidOperationException("Mass-nav scenarioRuntime.runtimeCapacity must be explicitly configured.");
        }

        if (Panel == null)
        {
            throw new InvalidOperationException("Mass-nav scenarioRuntime.panel must be explicitly configured.");
        }

        Panel.Validate();

        if (PanelControls == null)
        {
            throw new InvalidOperationException("Mass-nav scenarioRuntime.panelControls must be explicitly configured.");
        }

        RuntimeCapacity.Validate(this);
        PanelControls.Validate();
    }
}

public enum MassNavigationPanelMode : byte
{
    Owned = 1,
    Hidden = 2,
    External = 3,
}

public sealed class MassNavigationPanelConfig
{
    private MassNavigationPanelMode _parsedMode;

    public string Mode { get; set; } = string.Empty;

    [JsonIgnore]
    public MassNavigationPanelMode ParsedMode => _parsedMode;

    [JsonIgnore]
    public bool IsOwned => _parsedMode == MassNavigationPanelMode.Owned;

    public void Validate()
    {
        _parsedMode = Mode switch
        {
            "Owned" => MassNavigationPanelMode.Owned,
            "Hidden" => MassNavigationPanelMode.Hidden,
            "External" => MassNavigationPanelMode.External,
            "" => throw new InvalidOperationException("Mass-nav scenarioRuntime.panel.mode must be explicit."),
            _ => throw new InvalidOperationException(
                $"Mass-nav scenarioRuntime.panel.mode '{Mode}' is not configured.")
        };
    }
}

public sealed class MassNavigationRuntimeCapacityConfig
{
    public int NavigationGroupCapacity { get; set; }
    public int GroupMembershipAgentCapacity { get; set; }
    public int SelectionMemberScratchCapacity { get; set; }
    public int GroupMemberCapacity { get; set; }
    public int OrderIngestionTokenCapacity { get; set; }
    public int OrderIngestionMemberCapacity { get; set; }
    public int LoadedChunkCapacity { get; set; }
    public int MetadataTeamCapacity { get; set; }

    public void Validate(MassNavigationScenarioRuntimeConfig scenarioRuntime)
    {
        if (scenarioRuntime == null)
        {
            throw new InvalidOperationException("Mass-nav runtimeCapacity validation requires scenarioRuntime.");
        }

        RequirePositive(NavigationGroupCapacity, "navigationGroupCapacity");
        RequirePositive(GroupMembershipAgentCapacity, "groupMembershipAgentCapacity");
        RequirePositive(SelectionMemberScratchCapacity, "selectionMemberScratchCapacity");
        RequirePositive(GroupMemberCapacity, "groupMemberCapacity");
        RequirePositive(OrderIngestionTokenCapacity, "orderIngestionTokenCapacity");
        RequirePositive(OrderIngestionMemberCapacity, "orderIngestionMemberCapacity");
        RequirePositive(LoadedChunkCapacity, "loadedChunkCapacity");
        RequirePositive(MetadataTeamCapacity, "metadataTeamCapacity");

        if (SelectionMemberScratchCapacity < scenarioRuntime.InitialSelectedEntityCapacity)
        {
            throw new InvalidOperationException(
                "Mass-nav scenarioRuntime.runtimeCapacity.selectionMemberScratchCapacity must be >= scenarioRuntime.initialSelectedEntityCapacity.");
        }

        if (GroupMemberCapacity < scenarioRuntime.InitialSelectedEntityCapacity)
        {
            throw new InvalidOperationException(
                "Mass-nav scenarioRuntime.runtimeCapacity.groupMemberCapacity must be >= scenarioRuntime.initialSelectedEntityCapacity.");
        }

        if (OrderIngestionTokenCapacity < NavigationGroupCapacity)
        {
            throw new InvalidOperationException(
                "Mass-nav scenarioRuntime.runtimeCapacity.orderIngestionTokenCapacity must be >= scenarioRuntime.runtimeCapacity.navigationGroupCapacity.");
        }

        if (OrderIngestionMemberCapacity < scenarioRuntime.InitialSelectedEntityCapacity)
        {
            throw new InvalidOperationException(
                "Mass-nav scenarioRuntime.runtimeCapacity.orderIngestionMemberCapacity must be >= scenarioRuntime.initialSelectedEntityCapacity.");
        }
    }

    public void ValidateForScenario(int teamCount, int agentsPerTeam)
    {
        if (teamCount <= 0)
        {
            throw new InvalidOperationException("Mass-nav runtimeCapacity scenario validation requires a positive team count.");
        }

        long authoredAgentCount = (long)teamCount * agentsPerTeam;
        if (authoredAgentCount > GroupMembershipAgentCapacity)
        {
            throw new InvalidOperationException(
                $"Mass-nav scenarioRuntime.runtimeCapacity.groupMembershipAgentCapacity {GroupMembershipAgentCapacity} is smaller than authored scenario agent count {authoredAgentCount}.");
        }

        if (teamCount > MetadataTeamCapacity)
        {
            throw new InvalidOperationException(
                $"Mass-nav scenarioRuntime.runtimeCapacity.metadataTeamCapacity {MetadataTeamCapacity} is smaller than authored scenario team count {teamCount}.");
        }
    }

    public void ValidateForStreaming(MassNavigationWorldConfig world, MassNavigationViewResidencyConfig viewResidency)
    {
        if (world == null)
        {
            throw new InvalidOperationException("Mass-nav runtimeCapacity streaming validation requires world config.");
        }

        if (viewResidency == null)
        {
            throw new InvalidOperationException("Mass-nav runtimeCapacity streaming validation requires viewResidency config.");
        }

        int minimumWindowChunkCapacity = CountSquareChunksForRadius(
            viewResidency.RadiusCm,
            world.StreamingChunkSizeCm);
        if (LoadedChunkCapacity < minimumWindowChunkCapacity)
        {
            throw new InvalidOperationException(
                $"Mass-nav scenarioRuntime.runtimeCapacity.loadedChunkCapacity {LoadedChunkCapacity} is smaller than one view-residency window chunk count {minimumWindowChunkCapacity}.");
        }
    }

    public void ValidateForPanelControls(int teamCount, MassNavigationPanelControlsConfig panelControls)
    {
        if (teamCount <= 0)
        {
            throw new InvalidOperationException("Mass-nav runtimeCapacity panel validation requires a positive team count.");
        }

        if (panelControls == null)
        {
            throw new InvalidOperationException("Mass-nav runtimeCapacity panel validation requires panelControls.");
        }

        long maxPanelAgentCount = (long)teamCount * panelControls.MaxAgentsPerTeam;
        if (maxPanelAgentCount > GroupMembershipAgentCapacity)
        {
            throw new InvalidOperationException(
                $"Mass-nav scenarioRuntime.runtimeCapacity.groupMembershipAgentCapacity {GroupMembershipAgentCapacity} is smaller than panel max agent count {maxPanelAgentCount}.");
        }
    }

    private static void RequirePositive(int value, string fieldName)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"Mass-nav scenarioRuntime.runtimeCapacity.{fieldName} must be > 0.");
        }
    }

    private static int CountSquareChunksForRadius(int radiusCm, int chunkSizeCm)
    {
        if (radiusCm <= 0 || chunkSizeCm <= 0)
        {
            throw new InvalidOperationException("Mass-nav streaming chunk capacity validation requires positive radius and chunk size.");
        }

        int chunkRadius = (radiusCm + chunkSizeCm - 1) / chunkSizeCm;
        int span = checked((chunkRadius * 2) + 1);
        return checked(span * span);
    }
}

public sealed class MassNavigationPanelControlsConfig
{
    public int MaxAgentsPerTeam { get; set; }
    public int TotalAgentStep { get; set; }
    public int[] TotalAgentPresets { get; set; } = Array.Empty<int>();
    public float PanelRefreshIntervalSeconds { get; set; }
    public float ViewResidencyRetainSecondsStep { get; set; }
    public int SimulationBudgetStepMs { get; set; }
    public int SimulationBudgetMinMs { get; set; }
    public int SimulationBudgetMaxMs { get; set; }
    public int SimulationSliceStep { get; set; }
    public int SimulationSliceMin { get; set; }
    public int SimulationSliceMax { get; set; }
    public int EnginePolicyHzStep { get; set; }
    public int EnginePolicyHzMin { get; set; }
    public int EnginePolicyHzMax { get; set; }
    public int EnginePolicyMaxStepsStep { get; set; }
    public int EnginePolicyMaxStepsMin { get; set; }
    public int EnginePolicyMaxStepsMax { get; set; }
    public int ArrivalTimeoutStepMs { get; set; }
    public int ArrivalProgressStepCm { get; set; }
    public int ArrivalWakePushStepCm { get; set; }
    public int ArrivalRetryStep { get; set; }
    public int FlowIterationStep { get; set; }
    public int FlowCadenceHzStep { get; set; }

    public void Validate()
    {
        if (MaxAgentsPerTeam <= 0)
        {
            throw new InvalidOperationException("Mass-nav scenarioRuntime.panelControls.maxAgentsPerTeam must be > 0.");
        }

        if (TotalAgentStep <= 0)
        {
            throw new InvalidOperationException("Mass-nav scenarioRuntime.panelControls.totalAgentStep must be > 0.");
        }

        if (TotalAgentPresets == null || TotalAgentPresets.Length == 0)
        {
            throw new InvalidOperationException("Mass-nav scenarioRuntime.panelControls.totalAgentPresets must declare at least one value.");
        }

        var seen = new HashSet<int>(TotalAgentPresets.Length);
        for (int i = 0; i < TotalAgentPresets.Length; i++)
        {
            int preset = TotalAgentPresets[i];
            if (preset <= 0)
            {
                throw new InvalidOperationException(
                    $"Mass-nav scenarioRuntime.panelControls.totalAgentPresets[{i}] must be > 0.");
            }

            if (!seen.Add(preset))
            {
                throw new InvalidOperationException(
                    $"Mass-nav scenarioRuntime.panelControls.totalAgentPresets contains duplicate total {preset}.");
            }
        }

        RequirePositive(PanelRefreshIntervalSeconds, "panelRefreshIntervalSeconds");
        RequirePositive(ViewResidencyRetainSecondsStep, "viewResidencyRetainSecondsStep");
        RequirePositive(SimulationBudgetStepMs, "simulationBudgetStepMs");
        RequireRange(SimulationBudgetMinMs, SimulationBudgetMaxMs, "simulationBudget");
        RequirePositive(SimulationSliceStep, "simulationSliceStep");
        RequireRange(SimulationSliceMin, SimulationSliceMax, "simulationSlice");
        RequirePositive(EnginePolicyHzStep, "enginePolicyHzStep");
        RequireNonNegativeRange(EnginePolicyHzMin, EnginePolicyHzMax, "enginePolicyHz");
        RequirePositive(EnginePolicyMaxStepsStep, "enginePolicyMaxStepsStep");
        RequireRange(EnginePolicyMaxStepsMin, EnginePolicyMaxStepsMax, "enginePolicyMaxSteps");
        RequirePositive(ArrivalTimeoutStepMs, "arrivalTimeoutStepMs");
        RequirePositive(ArrivalProgressStepCm, "arrivalProgressStepCm");
        RequirePositive(ArrivalWakePushStepCm, "arrivalWakePushStepCm");
        RequirePositive(ArrivalRetryStep, "arrivalRetryStep");
        RequirePositive(FlowIterationStep, "flowIterationStep");
        RequirePositive(FlowCadenceHzStep, "flowCadenceHzStep");
    }

    public void ValidateForTeamCount(int teamCount, int agentsPerTeam)
    {
        if (teamCount <= 0)
        {
            throw new InvalidOperationException("Mass-nav panelControls validation requires a positive team count.");
        }

        if (agentsPerTeam > MaxAgentsPerTeam)
        {
            throw new InvalidOperationException(
                $"Mass-nav scenario.agentsPerTeam {agentsPerTeam} exceeds scenarioRuntime.panelControls.maxAgentsPerTeam {MaxAgentsPerTeam}.");
        }

        if (TotalAgentStep % teamCount != 0)
        {
            throw new InvalidOperationException(
                $"Mass-nav scenarioRuntime.panelControls.totalAgentStep {TotalAgentStep} must divide evenly across {teamCount} teams.");
        }

        long maxTotal = (long)MaxAgentsPerTeam * teamCount;
        for (int i = 0; i < TotalAgentPresets.Length; i++)
        {
            int preset = TotalAgentPresets[i];
            if (preset % teamCount != 0)
            {
                throw new InvalidOperationException(
                    $"Mass-nav scenarioRuntime.panelControls.totalAgentPresets[{i}] {preset} must divide evenly across {teamCount} teams.");
            }

            if (preset > maxTotal)
            {
                throw new InvalidOperationException(
                    $"Mass-nav scenarioRuntime.panelControls.totalAgentPresets[{i}] {preset} exceeds max total {maxTotal}.");
            }
        }
    }

    private static void RequirePositive(int value, string fieldName)
    {
        if (value <= 0)
        {
            throw new InvalidOperationException($"Mass-nav scenarioRuntime.panelControls.{fieldName} must be > 0.");
        }
    }

    private static void RequirePositive(float value, string fieldName)
    {
        if (!(value > 0f))
        {
            throw new InvalidOperationException($"Mass-nav scenarioRuntime.panelControls.{fieldName} must be > 0.");
        }
    }

    private static void RequireRange(int min, int max, string fieldName)
    {
        if (min <= 0 || max < min)
        {
            throw new InvalidOperationException(
                $"Mass-nav scenarioRuntime.panelControls.{fieldName} range must have min > 0 and max >= min.");
        }
    }

    private static void RequireNonNegativeRange(int min, int max, string fieldName)
    {
        if (min < 0 || max < min)
        {
            throw new InvalidOperationException(
                $"Mass-nav scenarioRuntime.panelControls.{fieldName} range must have min >= 0 and max >= min.");
        }
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
            throw new InvalidOperationException($"MassCrowd runtime config '{relativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"MassCrowd runtime config '{relativePath}' must use Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"MassCrowd runtime requires config '{relativePath}' through ConfigPipeline.");
        }

        return MassNavigationConfig.Load(merged);
    }
}

public sealed class MassNavigationCameraProfilesConfig
{
    public string TacticalProfileId { get; set; } = string.Empty;
    public string StrategicProfileId { get; set; } = string.Empty;
    public MassNavigationCameraRequestPolicyConfig RequestPolicy { get; set; } = new();

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

        RequestPolicy.Validate();
    }
}

public sealed class MassNavigationCameraRequestPolicyConfig
{
    public float BlendDurationSeconds { get; set; }
    public bool ResetRuntimeState { get; set; }
    public bool SnapToFollowTargetWhenAvailable { get; set; }
    public float StrategicTargetXCm { get; set; }
    public float StrategicTargetYCm { get; set; }

    public void Validate()
    {
        if (BlendDurationSeconds < 0f)
        {
            throw new InvalidOperationException("Mass-nav cameraProfiles.requestPolicy.blendDurationSeconds must be >= 0.");
        }
    }
}

public sealed class MassNavigationMinimapConfig
{
    private MinimapPreset _parsedInitialPreset;

    public bool Visible { get; set; }
    public string InitialPreset { get; set; } = string.Empty;
    public float FollowCameraHalfExtentCm { get; set; }
    public bool RotateWithCamera { get; set; }

    [JsonIgnore]
    public MinimapPreset ParsedInitialPreset => _parsedInitialPreset;

    public void Validate()
    {
        _parsedInitialPreset = InitialPreset switch
        {
            "RtsFullMap" => MinimapPreset.RtsFullMap,
            "FollowCamera" => MinimapPreset.FollowCamera,
            "FollowEntity" => throw new InvalidOperationException(
                "Mass-nav minimap.initialPreset cannot be FollowEntity without an authored follow entity."),
            "" => throw new InvalidOperationException("Mass-nav minimap.initialPreset must be a non-empty semantic string."),
            _ => throw new InvalidOperationException(
                $"Mass-nav minimap.initialPreset '{InitialPreset}' is not configured. Use RtsFullMap or FollowCamera.")
        };

        if (FollowCameraHalfExtentCm <= 0f)
        {
            throw new InvalidOperationException("Mass-nav minimap.followCameraHalfExtentCm must be > 0.");
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
            throw new InvalidOperationException("Mass-nav presentation requires an explicit scenarioRuntime section.");
        }

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

        if (!scenarioRuntime.AutoSpawnConfiguredScenario)
        {
            if (Teams.Length != 0)
            {
                throw new InvalidOperationException(
                    "Mass-nav presentation.teams must be empty when scenarioRuntime.autoSpawnConfiguredScenario is false; externally-authored scenarios must author agent templates in their own config.");
            }

            return;
        }

        RequireNonEmpty(BlockerPerformerId, nameof(BlockerPerformerId));
        RequireNonEmpty(HotspotPerformerId, nameof(HotspotPerformerId));
        RequireNonEmpty(HotspotTemplateId, nameof(HotspotTemplateId));

        if (Teams.Length != scenario.Teams.Length)
        {
            throw new InvalidOperationException("Mass-nav presentation team style count must match scenario teams.");
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

public sealed class MassFlowSolverConfig
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
            throw new InvalidOperationException($"Mass-nav solver requires {name} > 0.");
        }
    }

    private static void RequireNonNegative(int value, string name)
    {
        if (value < 0)
        {
            throw new InvalidOperationException($"Mass-nav solver requires {name} >= 0.");
        }
    }

    private static void RequireDivisible(int value, int divisor, string valueName, string divisorName)
    {
        if (value % divisor != 0)
        {
            throw new InvalidOperationException($"Mass-nav solver requires {valueName} to be divisible by {divisorName}.");
        }
    }

    private static void RequireGridCapacity(int width, int height, string label)
    {
        if ((long)width * height > int.MaxValue)
        {
            throw new InvalidOperationException($"Mass-nav solver {label} is too large for managed SoA arrays.");
        }
    }

    private static void RequireOrderedPlayArea(float min, float max, int fieldSize, string minName, string maxName)
    {
        if (!(min >= 0f) || !(max <= fieldSize) || !(min <= max))
        {
            throw new InvalidOperationException(
                $"Mass-nav solver requires ordered {minName}/{maxName} inside the configured field.");
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

    public void Validate(MassFlowSolverConfig solver)
    {
        if (solver == null)
        {
            throw new InvalidOperationException("Mass-nav world validation requires an explicit solver section.");
        }

        if (HotZones.Length <= 0)
        {
            throw new InvalidOperationException("Mass-nav world requires at least one configured hotspot debug landmark.");
        }

        if (SolverWindowWidthCm != solver.FieldWidthCm ||
            SolverWindowHeightCm != solver.FieldHeightCm)
        {
            throw new InvalidOperationException(
                $"Mass-nav world solver window must match solver field size ({solver.FieldWidthCm}x{solver.FieldHeightCm} cm).");
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
    public MassNavigationScenarioSpawnLayoutConfig SpawnLayout { get; set; } = new();

    public void Validate(MassNavigationScenarioRuntimeConfig scenarioRuntime)
    {
        if (scenarioRuntime == null)
        {
            throw new InvalidOperationException("Mass-nav scenario validation requires an explicit scenarioRuntime section.");
        }

        if (AgentsPerTeam < 0)
        {
            throw new InvalidOperationException("Mass-nav config requires AgentsPerTeam >= 0.");
        }

        if (Teams.Length <= 0)
        {
            throw new InvalidOperationException("Mass-nav config requires at least one team.");
        }

        var seenIds = new HashSet<int>(Teams.Length);
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

        scenarioRuntime.RuntimeCapacity.ValidateForScenario(Teams.Length, AgentsPerTeam);
        scenarioRuntime.PanelControls.ValidateForTeamCount(Teams.Length, AgentsPerTeam);
        scenarioRuntime.RuntimeCapacity.ValidateForPanelControls(Teams.Length, scenarioRuntime.PanelControls);

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
            "" => throw new InvalidOperationException("Mass-nav scenario.spawnLayout.kind must be a non-empty semantic string."),
            _ => throw new InvalidOperationException(
                $"Mass-nav scenario.spawnLayout.kind '{Kind}' is not configured.")
        };

        if (OrbitRadiusCm <= 0f)
        {
            throw new InvalidOperationException("Mass-nav scenario.spawnLayout.orbitRadiusCm must be > 0.");
        }
    }
}

public sealed class MassNavigationScenarioTeamConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
