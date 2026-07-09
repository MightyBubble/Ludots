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
        int[] playerOwnerIdsByScenarioTeam = ResolveScenarioTeamPlayerOwners(session, teamIds);
        simulation.ConfigureScenarioTeams(teamIds);
        ConfigureRelationships(simulation.Config);
        MassNavigationAgentLayer scenarioAgentLayer = ResolveScenarioAgentLayer(authoring, simulation.Config);
        simulation.MassNavigationFlow.Reset(
            teamIds,
            simulation.AgentsPerTeam,
            simulation.Config.AgentProfiles,
            scenarioAgentLayer,
            simulation.Config.Scenario.SpawnLayout);

        for (int teamIndex = 0; teamIndex < teamIds.Length; teamIndex++)
        {
            int teamId = teamIds[teamIndex];
            string teamName = simulation.Config.Scenario.Teams[teamIndex].Name;
            teamLookup.Register(teamId, RelationshipTeamBootstrapper.EnsureTeamEntity(engine.World, teamLookup, teamId, teamName));
        }

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
            float navMass = simulation.MassNavigationFlow.GetNavMass(i);
            float visualScale = simulation.MassNavigationFlow.GetVisualScale(i);
            float bodyRadiusCm = simulation.MassNavigationFlow.GetBodyRadiusCm(i);
            float speedCmPerSecond = simulation.MassNavigationFlow.GetSpeedCmPerSecond(i);
            bool heavy = simulation.MassNavigationFlow.IsHeavyProfile(i);
            string templateId = simulation.Config.Presentation.ResolveAgentTemplateId(teamId, heavy);
            authoring.ValidateTemplate(templateId);
            EnqueueSpawn(
                spawnQueue,
                mapId,
                templateId,
                Fix64Vec2.FromInt((int)MathF.Round(worldXCm), (int)MathF.Round(worldYCm)),
                teamIdOverride: teamId,
                playerOwnerIdOverride: ResolvePlayerOwnerId(teamIds, playerOwnerIdsByScenarioTeam, teamId));
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

    private static void ConfigureRelationships(MassNavigationConfig config)
    {
        TeamManager.LoadConfig(config.TeamRelationships);
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

    private static int[] ResolveScenarioTeamPlayerOwners(MapSession session, ReadOnlySpan<int> scenarioTeamIds)
    {
        MapConfig mapConfig = session.MapConfig
            ?? throw new InvalidOperationException($"MassNavigation runtime map '{session.MapId.Value}' has no MapConfig.");
        int[] playerOwnerIdsByScenarioTeam = new int[scenarioTeamIds.Length];
        for (int i = 0; i < mapConfig.Players.Count; i++)
        {
            PlayerBindingData binding = mapConfig.Players[i];
            int teamIndex = IndexOfScenarioTeam(scenarioTeamIds, binding.TeamId);
            if (teamIndex < 0)
            {
                continue;
            }

            if (playerOwnerIdsByScenarioTeam[teamIndex] > 0)
            {
                throw new InvalidOperationException(
                    $"MassNavigation runtime map '{session.MapId.Value}' binds multiple players to scenario team {binding.TeamId}; generated agents require one PlayerOwner per team.");
            }

            playerOwnerIdsByScenarioTeam[teamIndex] = binding.PlayerId;
        }

        return playerOwnerIdsByScenarioTeam;
    }

    private static int ResolvePlayerOwnerId(ReadOnlySpan<int> scenarioTeamIds, ReadOnlySpan<int> playerOwnerIdsByScenarioTeam, int teamId)
    {
        int teamIndex = IndexOfScenarioTeam(scenarioTeamIds, teamId);
        return teamIndex >= 0 ? playerOwnerIdsByScenarioTeam[teamIndex] : 0;
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
        int teamIdOverride = 0,
        int playerOwnerIdOverride = 0,
        RuntimeEntitySpawnComponentPatch[]? componentPatches = null)
    {
        var request = new RuntimeEntitySpawnRequest
        {
            Kind = RuntimeEntitySpawnKind.Template,
            TemplateId = templateId,
            MapId = mapId,
            WorldPositionCm = worldPosition,
            HasWorldPosition = 1,
            TeamIdOverride = teamIdOverride,
            PlayerOwnerIdOverride = playerOwnerIdOverride,
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
