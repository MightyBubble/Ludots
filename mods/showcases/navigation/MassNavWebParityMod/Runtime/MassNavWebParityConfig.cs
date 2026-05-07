using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Gameplay.Teams;

namespace MassNavWebParityMod.Runtime;

public sealed class MassNavWebParityConfig
{
    public string MapId { get; set; } = string.Empty;
    public MassNavWorldConfig? World { get; set; }
    public MassNavScenarioConfig Scenario { get; set; } = new();
    public TeamConfig TeamRelationships { get; set; } = new();
    public MassNavFlowTuning Flow { get; set; } = new();
    public MassNavArrivalTuning Arrival { get; set; } = new();
    public MassNavAvoidanceTuning Avoidance { get; set; } = new();
    public MassNavCrowdSemantics Semantics { get; set; } = new();

    public static MassNavWebParityConfig Load(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        MassNavWebParityConfig? config = JsonSerializer.Deserialize<MassNavWebParityConfig>(stream, options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize mass-nav web parity config.");
        }

        config.Validate();
        return config;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(MapId))
        {
            throw new InvalidOperationException("Mass-nav config requires a non-empty map id.");
        }

        Scenario.Validate();
        if (World == null)
        {
            throw new InvalidOperationException("Mass-nav config requires an explicit world section.");
        }

        World.Validate();

        if (TeamRelationships.Relationships == null)
        {
            TeamRelationships.Relationships = new List<RelationshipEntry>();
        }

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
}

public sealed class MassNavWorldConfig
{
    private int _activeHotZoneIndex = -1;

    public int WorldWidthCm { get; set; }
    public int WorldHeightCm { get; set; }
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
    public MassNavHotZoneConfig[] HotZones { get; set; } = Array.Empty<MassNavHotZoneConfig>();

    [JsonIgnore]
    public MassNavHotZoneConfig ActiveHotZone => _activeHotZoneIndex >= 0 && _activeHotZoneIndex < HotZones.Length
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

    public bool TryGetHotZone(string hotZoneId, out MassNavHotZoneConfig hotZone)
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
        if (WorldWidthCm <= 0 || WorldHeightCm <= 0)
        {
            throw new InvalidOperationException("Mass-nav world requires positive WorldWidthCm and WorldHeightCm.");
        }

        if (HotZones.Length <= 0)
        {
            throw new InvalidOperationException("Mass-nav world requires at least one configured hotspot debug landmark.");
        }

        if (SolverWindowWidthCm != MassNavWebParitySimState.FieldWidthCm ||
            SolverWindowHeightCm != MassNavWebParitySimState.FieldHeightCm)
        {
            throw new InvalidOperationException(
                $"Mass-nav world solver window must be explicit and match the current SoA solver cache ({MassNavWebParitySimState.FieldWidthCm}x{MassNavWebParitySimState.FieldHeightCm} cm).");
        }

        float minWorldX = WorldWidthCm * -0.5f;
        float maxWorldX = WorldWidthCm * 0.5f;
        float minWorldY = WorldHeightCm * -0.5f;
        float maxWorldY = WorldHeightCm * 0.5f;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < HotZones.Length; i++)
        {
            MassNavHotZoneConfig zone = HotZones[i];
            zone.Validate();
            if (!ids.Add(zone.Id))
            {
                throw new InvalidOperationException($"Mass-nav world contains duplicate hot zone id '{zone.Id}'.");
            }

            float zoneMinX = zone.CenterXCm - (zone.WidthCm * 0.5f);
            float zoneMaxX = zone.CenterXCm + (zone.WidthCm * 0.5f);
            float zoneMinY = zone.CenterYCm - (zone.HeightCm * 0.5f);
            float zoneMaxY = zone.CenterYCm + (zone.HeightCm * 0.5f);
            if (zoneMinX < minWorldX || zoneMaxX > maxWorldX ||
                zoneMinY < minWorldY || zoneMaxY > maxWorldY)
            {
                throw new InvalidOperationException($"Mass-nav hot zone '{zone.Id}' must be fully inside the configured world bounds.");
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

    public float ClampSolverWindowCenterX(float worldXCm)
    {
        return ClampWindowCenterX(worldXCm, SolverWindowWidthCm);
    }

    public float ClampSolverWindowCenterY(float worldYCm)
    {
        return ClampWindowCenterY(worldYCm, SolverWindowHeightCm);
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

    private int ClampWindowCenterX(float worldXCm, int widthCm)
    {
        float halfWidth = widthCm * 0.5f;
        return (int)MathF.Round(Math.Clamp(worldXCm, (WorldWidthCm * -0.5f) + halfWidth, (WorldWidthCm * 0.5f) - halfWidth));
    }

    private int ClampWindowCenterY(float worldYCm, int heightCm)
    {
        float halfHeight = heightCm * 0.5f;
        return (int)MathF.Round(Math.Clamp(worldYCm, (WorldHeightCm * -0.5f) + halfHeight, (WorldHeightCm * 0.5f) - halfHeight));
    }
}

public sealed class MassNavHotZoneConfig
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

public sealed class MassNavScenarioConfig
{
    public int AgentsPerTeam { get; set; }
    public int InitialSelectedTeamId { get; set; }
    public MassNavScenarioTeamConfig[] Teams { get; set; } = Array.Empty<MassNavScenarioTeamConfig>();

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
            MassNavScenarioTeamConfig team = Teams[i];
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

public sealed class MassNavScenarioTeamConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
