using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ludots.Core.Gameplay.Teams;

namespace MassNavWebParityMod.Runtime;

public sealed class MassNavWebParityConfig
{
    public string MapId { get; set; } = string.Empty;
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
