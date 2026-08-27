using Arch.Core;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.MassNavigation.Systems;

internal static class MassNavigationScenarioBootstrap
{
    public static void SpawnConfiguredScenario(GameEngine engine, MassNavigationSimulationRuntime simulation, TeamEntityLookup teamLookup)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(teamLookup);

        MassNavigationAuthoringContract authoring = MassNavigationAuthoringContract.Require(engine, simulation.Config);
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("MassNavigation runtime requires RuntimeEntitySpawnQueue.");

        simulation.AgentState.Reset();
        ReadOnlySpan<int> teamIds = simulation.TeamIds;
        MapSession session = RequireCurrentMapSession(engine, simulation.Config.MapId);
        PlayerEntityLookup playerLookup = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
            ?? throw new InvalidOperationException("MassNavigation scenario requires PlayerEntityLookup.");
        simulation.ConfigureScenarioTeams(teamIds);
        MassNavigationAgentLayer scenarioAgentLayer = ResolveScenarioAgentLayer(authoring, simulation.Config);
        simulation.MassNavigationFlow.Reset(
            teamIds,
            simulation.AgentsPerTeam,
            simulation.Config.AgentProfiles,
            scenarioAgentLayer,
            simulation.Config.Scenario.SpawnLayout);

        var teamDomains = new Entity[teamIds.Length];
        var controlOwners = new Entity[teamIds.Length];
        for (int teamIndex = 0; teamIndex < teamIds.Length; teamIndex++)
        {
            int teamId = teamIds[teamIndex];
            string teamName = simulation.Config.Scenario.Teams[teamIndex].Name;
            teamDomains[teamIndex] = RelationshipTeamBootstrapper.EnsureTeamEntity(engine.World, teamLookup, teamId, teamName);
            controlOwners[teamIndex] = ResolveScenarioTeamControlOwner(session, playerLookup, teamId);
        }

        ConfigureRelationships(engine, simulation.Config, teamIds, teamDomains);

        int requested = simulation.MassNavigationFlow.UnitCount + simulation.HotZones.Length;
        if (spawnQueue.FreeCapacity < requested)
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime requires RuntimeEntitySpawnQueue free capacity {requested}, actual {spawnQueue.FreeCapacity}.");
        }

        MapId mapId = session.MapId;
        for (int i = 0; i < simulation.MassNavigationFlow.UnitCount; i++)
        {
            int teamId = simulation.MassNavigationFlow.GetTeam(i);
            float xCm = simulation.MassNavigationFlow.GetPositionX(i);
            float yCm = simulation.MassNavigationFlow.GetPositionY(i);
            float worldXCm = simulation.ToWorldXCm(xCm);
            float worldYCm = simulation.ToWorldYCm(yCm);
            bool heavy = simulation.MassNavigationFlow.IsHeavyProfile(i);
            string templateId = simulation.Config.Presentation.ResolveAgentTemplateId(teamId, heavy);
            authoring.ValidateTemplate(templateId);
            EnqueueSpawn(
                spawnQueue,
                mapId,
                templateId,
                Fix64Vec2.FromInt((int)MathF.Round(worldXCm), (int)MathF.Round(worldYCm)),
                ownershipSource: controlOwners[IndexOfScenarioTeam(teamIds, teamId)],
                membershipTarget: teamDomains[IndexOfScenarioTeam(teamIds, teamId)]);
        }

        string hotspotTemplateId = simulation.Config.Presentation.HotspotTemplateId;
        authoring.ValidateTemplate(hotspotTemplateId);
        ReadOnlySpan<MassNavigationHotZoneConfig> hotZones = simulation.HotZones;
        for (int i = 0; i < hotZones.Length; i++)
        {
            MassNavigationHotZoneConfig zone = hotZones[i];
            EnqueueSpawn(
                spawnQueue,
                mapId,
                hotspotTemplateId,
                Fix64Vec2.FromInt(zone.CenterXCm, zone.CenterYCm));
        }

        simulation.MarkScenarioSpawned();
        simulation.MarkStructuralChange();
    }

    private static void ConfigureRelationships(
        GameEngine engine,
        MassNavigationConfig config,
        ReadOnlySpan<int> teamIds,
        ReadOnlySpan<Entity> teamDomains)
    {
        RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
            ?? throw new InvalidOperationException("MassNavigation scenario requires RelationshipRuntime.");
        RelationshipTypeRegistry types = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
            ?? throw new InvalidOperationException("MassNavigation scenario requires RelationshipTypeRegistry.");
        for (int a = 0; a < teamIds.Length; a++)
        {
            for (int b = 0; b < teamIds.Length; b++)
            {
                if (a == b)
                {
                    continue;
                }

                string stance = ResolveConfiguredStance(config.TeamRelationships, teamIds[a], teamIds[b]);
                relationships.EnsureLink(teamDomains[a], teamDomains[b], types.GetId(stance));
            }
        }
    }

    private static MassNavigationAgentLayer ResolveScenarioAgentLayer(
        MassNavigationAuthoringContract authoring,
        MassNavigationConfig config)
    {
        MassNavigationAgentLayer? resolved = null;
        for (int i = 0; i < config.Presentation.Teams.Length; i++)
        {
            MassNavigationTeamPresentationConfig team = config.Presentation.Teams[i];
            MassNavigationAgentLayer lightLayer = authoring.RequireAgentLayer(team.LightTemplateId);
            MassNavigationAgentLayer heavyLayer = authoring.RequireAgentLayer(team.HeavyTemplateId);
            RequireSameLayer(lightLayer, heavyLayer, team.LightTemplateId, team.HeavyTemplateId);

            if (resolved.HasValue)
            {
                RequireSameLayer(resolved.Value, lightLayer, "MassNavigation scenario agent layer", team.LightTemplateId);
            }
            else
            {
                resolved = lightLayer;
            }
        }

        return resolved ?? throw new InvalidOperationException("MassNavigation runtime requires at least one configured presentation team.");
    }

    private static void RequireSameLayer(
        MassNavigationAgentLayer expected,
        MassNavigationAgentLayer actual,
        string expectedLabel,
        string actualLabel)
    {
        if (expected.CategoryMask == actual.CategoryMask &&
            expected.InteractionMask == actual.InteractionMask)
        {
            return;
        }

        throw new InvalidOperationException(
            $"MassNavigation runtime scenario auto-spawn requires one explicit agent layer across generated agent templates; '{actualLabel}' differs from '{expectedLabel}'.");
    }

    private static Entity ResolveScenarioTeamControlOwner(
        MapSession session,
        PlayerEntityLookup players,
        int scenarioTeamId)
    {
        MapConfig mapConfig = session.MapConfig
            ?? throw new InvalidOperationException($"MassNavigation runtime map '{session.MapId.Value}' has no MapConfig.");
        Entity resolved = Entity.Null;
        for (int i = 0; i < mapConfig.Players.Count; i++)
        {
            PlayerBindingData binding = mapConfig.Players[i];
            if (binding.TeamId != scenarioTeamId)
            {
                continue;
            }

            if (resolved != Entity.Null)
            {
                throw new InvalidOperationException(
                    $"MassNavigation runtime map '{session.MapId.Value}' binds multiple control domains to scenario team {binding.TeamId}; generated agents require one explicit control owner per team.");
            }

            if (!players.TryGet(binding.PlayerId, out resolved))
            {
                throw new InvalidOperationException(
                    $"MassNavigation runtime map '{session.MapId.Value}' cannot resolve player representative {binding.PlayerId}.");
            }
        }

        return resolved;
    }

    private static string ResolveConfiguredStance(TeamConfig config, int sourceTeamId, int targetTeamId)
    {
        for (int i = 0; i < config.Relationships.Count; i++)
        {
            RelationshipEntry relation = config.Relationships[i];
            if (relation.TeamA == sourceTeamId && relation.TeamB == targetTeamId ||
                relation.Symmetric && relation.TeamA == targetTeamId && relation.TeamB == sourceTeamId)
            {
                return relation.Attitude;
            }
        }

        return config.DefaultRelationship;
    }

    private static int IndexOfScenarioTeam(ReadOnlySpan<int> scenarioTeamIds, int teamId)
    {
        for (int i = 0; i < scenarioTeamIds.Length; i++)
        {
            if (scenarioTeamIds[i] == teamId)
            {
                return i;
            }
        }

        return -1;
    }

    private static void EnqueueSpawn(
        RuntimeEntitySpawnQueue spawnQueue,
        MapId mapId,
        string templateId,
        Fix64Vec2 worldPosition,
        Entity? ownershipSource = null,
        Entity? membershipTarget = null,
        RuntimeEntitySpawnComponentPatch[]? componentPatches = null)
    {
        bool hasOwnershipSource = ownershipSource.HasValue && ownershipSource.Value != Entity.Null;
        bool hasMembershipTarget = membershipTarget.HasValue && membershipTarget.Value != Entity.Null;
        var request = new RuntimeEntitySpawnRequest
        {
            Kind = RuntimeEntitySpawnKind.Template,
            TemplateId = templateId,
            MapId = mapId,
            WorldPositionCm = worldPosition,
            HasWorldPosition = 1,
            OwnershipSource = hasOwnershipSource ? ownershipSource!.Value : Entity.Null,
            HasOwnershipSource = hasOwnershipSource ? (byte)1 : (byte)0,
            MembershipTarget = hasMembershipTarget ? membershipTarget!.Value : Entity.Null,
            HasMembershipTarget = hasMembershipTarget ? (byte)1 : (byte)0,
            ComponentPatches = componentPatches ?? Array.Empty<RuntimeEntitySpawnComponentPatch>(),
        };

        if (!spawnQueue.TryEnqueue(in request))
        {
            throw new InvalidOperationException("MassNavigation runtime failed to enqueue runtime entity spawn request.");
        }
    }

    private static MapSession RequireCurrentMapSession(GameEngine engine, string configuredMapId)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("MassNavigation runtime requires an active map session before scenario bootstrap.");
        if (!string.Equals(session.MapId.Value, configuredMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MassNavigation runtime scenario bootstrap requires active map '{configuredMapId}', got '{session.MapId.Value}'.");
        }

        return session;
    }

}
