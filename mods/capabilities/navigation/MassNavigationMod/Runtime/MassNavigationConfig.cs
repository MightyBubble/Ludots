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
    public MassNavigationCadenceConfig Cadence { get; set; } = new();
    public MassNavigationAgentProfileSetConfig AgentProfiles { get; set; } = new();
    public TeamConfig TeamRelationships { get; set; } = new();
    public MassFlowTuning Flow { get; set; } = new();
    public MassFlowArrivalTuning Arrival { get; set; } = new();
    public MassFlowAvoidanceTuning Avoidance { get; set; } = new();
    public MassNavigationCrowdSemantics Semantics { get; set; } = new();
    public MassNavigationBakeDataConfig BakeData { get; set; } = new();
    public MassNavigationShowcaseConfig Showcase { get; set; } = new();

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
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

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
        RequireProperty(root, "cadence");
        RequireProperty(root, "agentProfiles");
        RequireProperty(root, "teamRelationships");
        RequireProperty(root, "flow");
        RequireProperty(root, "arrival");
        RequireProperty(root, "avoidance");
        RequireProperty(root, "semantics");
        RequireProperty(root, "bakeData");

        JsonElement world = RequireProperty(root, "world");
        RequireProperty(world, "obstacles");
        JsonElement relationships = RequireProperty(root, "teamRelationships");
        RequireProperty(relationships, "defaultRelationship");
        RequireProperty(relationships, "relationships");
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"Mass-nav config requires explicit '{propertyName}' property.");
        }

        return value;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(MapId))
        {
            throw new InvalidOperationException("Mass-nav config requires a non-empty map id.");
        }

        Scenario.Validate();
        Presentation.Validate(Scenario);
        Cadence.Validate();
        AgentProfiles.Validate();
        if (World == null)
        {
            throw new InvalidOperationException("Mass-nav config requires an explicit world section.");
        }

        World.Validate();
        Semantics.Validate();
        BakeData.Validate();
        Showcase.Validate();

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

            if (!Enum.TryParse<TeamRelationship>(relation.Attitude, true, out _))
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
            !Enum.TryParse<TeamRelationship>(TeamRelationships.DefaultRelationship, true, out _))
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

        if (entry.MergePolicy != ConfigMergePolicy.DeepObject)
        {
            throw new InvalidOperationException($"MassNavigationMod config '{relativePath}' must use DeepObject merge policy.");
        }

        JsonObject? merged = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
        if (merged == null)
        {
            throw new InvalidOperationException($"MassNavigationMod requires config '{relativePath}' through ConfigPipeline.");
        }

        return MassNavigationConfig.Load(merged);
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

        var meshIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < HotZones.Length; i++)
        {
            MassNavigationHotZoneConfig zone = HotZones[i];
            zone.Validate();
            if (!ids.Add(zone.Id))
            {
                throw new InvalidOperationException($"Mass-nav world contains duplicate hot zone id '{zone.Id}'.");
            }

        }

        var obstacleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            if (string.Equals(HotZones[i].Id, hotZoneId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}

public sealed class MassNavigationBakeDataConfig
{
    public int MacroChunkColumns { get; set; }
    public int MacroChunkRows { get; set; }
    public int TargetStaticObstacleCount { get; set; }
    public bool HpaOverlayRequired { get; set; }
    public bool PathInspectorRequired { get; set; }
    public bool BakeOverlayRequired { get; set; }

    public void Validate()
    {
        if (MacroChunkColumns <= 0 || MacroChunkRows <= 0)
        {
            throw new InvalidOperationException("Mass-nav bakeData requires positive MacroChunkColumns and MacroChunkRows.");
        }

        if (TargetStaticObstacleCount < 0)
        {
            throw new InvalidOperationException("Mass-nav bakeData requires TargetStaticObstacleCount >= 0.");
        }
    }
}

public sealed class MassNavigationShowcaseConfig
{
    public string Id { get; set; } = "full_acceptance";
    public string Title { get; set; } = "Mass Navigation full acceptance cockpit";
    public string PanelMode { get; set; } = "Full";
    public string InitialStepId { get; set; } = string.Empty;
    public string[] VisibleStepIds { get; set; } = Array.Empty<string>();
    public string PlayerPerspective { get; set; } = "Open the guided showcase, run the current objective, and verify the visible pass signal.";
    public string ModAuthorPerspective { get; set; } = "Use MassNavigationConfig.json, Navigation/navmesh.json, Navigation/pathing.json, and baked Data/Navigation artifacts as the SDK contract.";

    [JsonIgnore]
    public bool FocusedPanel => string.Equals(PanelMode, "Focused", StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException("Mass-nav showcase config requires a non-empty Id.");
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            throw new InvalidOperationException($"Mass-nav showcase '{Id}' requires a non-empty Title.");
        }

        if (string.IsNullOrWhiteSpace(PanelMode))
        {
            throw new InvalidOperationException($"Mass-nav showcase '{Id}' requires a non-empty PanelMode.");
        }

        if (!string.Equals(PanelMode, "Full", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(PanelMode, "Focused", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Mass-nav showcase '{Id}' has unsupported PanelMode '{PanelMode}'.");
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
