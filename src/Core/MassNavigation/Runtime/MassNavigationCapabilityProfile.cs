using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Teams;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationCapabilityProfile
{
    [JsonRequired] public MassNavigationConfig Runtime { get; set; } = new();
    [JsonRequired] public MassNavigationSceneAuthoringConfig SceneAuthoring { get; set; } = new();

    public static MassNavigationCapabilityProfile Load(JsonObject profileObject)
    {
        ArgumentNullException.ThrowIfNull(profileObject);
        using var document = JsonDocument.Parse(profileObject.ToJsonString());
        return Load(document.RootElement);
    }

    public static MassNavigationCapabilityProfile Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var document = JsonDocument.Parse(stream);
        return Load(document.RootElement);
    }

    private static MassNavigationCapabilityProfile Load(JsonElement root)
    {
        MassNavigationCapabilityProfile? profile = root.Deserialize<MassNavigationCapabilityProfile>(
            StrictJsonOptions.CreateCamelCase());
        if (profile == null)
        {
            throw new InvalidOperationException("Failed to deserialize MassNavigation capability profile.");
        }

        profile.Runtime.Validate();
        profile.SceneAuthoring.Validate(profile.Runtime);
        return profile;
    }
}

public sealed class MassNavigationSceneAuthoringConfig
{
    [JsonRequired] public bool AutoSpawnConfiguredScenario { get; set; }
    [JsonRequired] public MassNavigationPresentationConfig? Presentation { get; set; }
    [JsonRequired] public MassNavigationScenarioConfig? Scenario { get; set; }
    [JsonRequired] public MassNavigationTeamRelationshipConfig? TeamRelationships { get; set; }

    public void Validate(MassNavigationConfig runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!AutoSpawnConfiguredScenario)
        {
            if (Presentation != null || Scenario != null || TeamRelationships != null)
            {
                throw new InvalidOperationException(
                    "MassNavigation sceneAuthoring must not carry presentation, scenario, or teamRelationships when autoSpawnConfiguredScenario is false.");
            }

            return;
        }

        MassNavigationScenarioConfig scenario = Scenario
            ?? throw new InvalidOperationException("MassNavigation sceneAuthoring requires scenario when auto-spawn is enabled.");
        if (scenario.SpawnLayout == null)
        {
            throw new InvalidOperationException("MassNavigation sceneAuthoring scenario requires spawnLayout when auto-spawn is enabled.");
        }

        scenario.Validate(runtime.Capacity);
        (Presentation ?? throw new InvalidOperationException(
            "MassNavigation sceneAuthoring requires presentation when auto-spawn is enabled."))
            .Validate(scenario, runtime.World);
        (TeamRelationships ?? throw new InvalidOperationException(
            "MassNavigation sceneAuthoring requires teamRelationships when auto-spawn is enabled."))
            .Validate(scenario.Teams);
    }
}

public sealed class MassNavigationTeamRelationshipConfig
{
    [JsonRequired] public string DefaultRelationship { get; set; } = string.Empty;
    [JsonRequired] public MassNavigationTeamRelationshipEntry[] Relationships { get; set; } = Array.Empty<MassNavigationTeamRelationshipEntry>();

    public void Validate(ReadOnlySpan<MassNavigationScenarioTeamConfig> teams)
    {
        if (!TeamManager.TryParseRelationship(DefaultRelationship, out _))
        {
            throw new InvalidOperationException(
                $"MassNavigation sceneAuthoring.teamRelationships.defaultRelationship is invalid: '{DefaultRelationship}'.");
        }

        var teamIds = new HashSet<int>(teams.Length);
        for (int i = 0; i < teams.Length; i++)
        {
            teamIds.Add(teams[i].Id);
        }

        for (int i = 0; i < Relationships.Length; i++)
        {
            MassNavigationTeamRelationshipEntry relationship = Relationships[i];
            if (!teamIds.Contains(relationship.TeamA) || !teamIds.Contains(relationship.TeamB))
            {
                throw new InvalidOperationException(
                    $"MassNavigation scene relationship [{relationship.TeamA},{relationship.TeamB}] references an unknown scene team.");
            }

            if (!TeamManager.TryParseRelationship(relationship.Attitude, out _))
            {
                throw new InvalidOperationException(
                    $"MassNavigation scene relationship [{relationship.TeamA},{relationship.TeamB}] has invalid attitude '{relationship.Attitude}'.");
            }
        }
    }

    public TeamConfig CreateTeamConfig()
    {
        var relationships = new List<RelationshipEntry>(Relationships.Length);
        for (int i = 0; i < Relationships.Length; i++)
        {
            MassNavigationTeamRelationshipEntry source = Relationships[i];
            relationships.Add(new RelationshipEntry
            {
                TeamA = source.TeamA,
                TeamB = source.TeamB,
                Attitude = source.Attitude,
                Symmetric = source.Symmetric,
            });
        }

        return new TeamConfig
        {
            DefaultRelationship = DefaultRelationship,
            Relationships = relationships,
        };
    }
}

public sealed class MassNavigationTeamRelationshipEntry
{
    [JsonRequired] public int TeamA { get; set; }
    [JsonRequired] public int TeamB { get; set; }
    [JsonRequired] public string Attitude { get; set; } = string.Empty;
    [JsonRequired] public bool Symmetric { get; set; }
}
