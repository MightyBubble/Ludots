using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Ludots.Core.MassCrowd.Runtime;
using System.Text.Json.Nodes;

namespace Ludots.Core.MassCrowd.Systems;

internal static class MassNavigationScenarioBootstrap
{
    public static void SpawnConfiguredScenario(GameEngine engine, MassNavigationSimulationRuntime simulation, TeamEntityLookup teamLookup)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(teamLookup);

        MassNavigationAuthoringContract authoring = MassNavigationAuthoringContract.Require(engine, simulation.Config);
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("MassCrowd runtime requires RuntimeEntitySpawnQueue.");

        simulation.AgentState.Reset();
        ReadOnlySpan<int> teamIds = simulation.TeamIds;
        simulation.ConfigureScenarioTeams(teamIds);
        ConfigureRelationships(simulation.Config);
        MassNavigationAgentLayer scenarioAgentLayer = ResolveScenarioAgentLayer(authoring, simulation.Config);
        simulation.MassFlow.Reset(
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

        int requested = simulation.MassFlow.UnitCount + simulation.HotZones.Length;
        if (spawnQueue.FreeCapacity < requested)
        {
            throw new InvalidOperationException(
                $"MassCrowd runtime requires RuntimeEntitySpawnQueue free capacity {requested}, actual {spawnQueue.FreeCapacity}.");
        }

        MapId mapId = RequireCurrentMapId(engine, simulation.Config.MapId);
        for (int i = 0; i < simulation.MassFlow.UnitCount; i++)
        {
            int teamId = simulation.MassFlow.GetTeam(i);
            float xCm = simulation.MassFlow.GetPositionX(i);
            float yCm = simulation.MassFlow.GetPositionY(i);
            float worldXCm = simulation.ToWorldXCm(xCm);
            float worldYCm = simulation.ToWorldYCm(yCm);
            float navMass = simulation.MassFlow.GetNavMass(i);
            float visualScale = simulation.MassFlow.GetVisualScale(i);
            float bodyRadiusCm = simulation.MassFlow.GetBodyRadiusCm(i);
            float speedCmPerSecond = simulation.MassFlow.GetSpeedCmPerSecond(i);
            bool heavy = simulation.MassFlow.IsHeavyProfile(i);
            string templateId = simulation.Config.Presentation.ResolveAgentTemplateId(teamId, heavy);
            authoring.ValidateTemplate(templateId);
            EnqueueSpawn(
                spawnQueue,
                mapId,
                templateId,
                Fix64Vec2.FromInt((int)MathF.Round(worldXCm), (int)MathF.Round(worldYCm)),
                teamIdOverride: teamId);
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

        return resolved ?? throw new InvalidOperationException("MassCrowd runtime requires at least one configured presentation team.");
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
            $"MassCrowd runtime scenario auto-spawn requires one explicit agent layer across generated agent templates; '{actualLabel}' differs from '{expectedLabel}'.");
    }

    private static void ValidateTemplate(GameEngine engine, string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new InvalidOperationException("MassCrowd runtime template id must be non-empty.");
        }

        if (!engine.MapLoader.TemplateRegistry.Contains(templateId))
        {
            throw new InvalidOperationException($"MassCrowd runtime requires configured entity template '{templateId}'.");
        }

        EntityTemplateKeyRegistry templateKeys = engine.GetService(CoreServiceKeys.EntityTemplateKeyRegistry)
            ?? throw new InvalidOperationException("MassCrowd runtime requires EntityTemplateKeyRegistry.");
        if (!templateKeys.TryGetId(templateId, out int templateKeyId) || templateKeyId <= 0)
        {
            throw new InvalidOperationException($"MassCrowd runtime template '{templateId}' was not registered in EntityTemplateKeyRegistry.");
        }
    }

    private static void EnqueueSpawn(
        RuntimeEntitySpawnQueue spawnQueue,
        MapId mapId,
        string templateId,
        Fix64Vec2 worldPosition,
        int teamIdOverride = 0,
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
            ComponentPatches = componentPatches ?? Array.Empty<RuntimeEntitySpawnComponentPatch>(),
        };

        if (!spawnQueue.TryEnqueue(in request))
        {
            throw new InvalidOperationException("MassCrowd runtime failed to enqueue runtime entity spawn request.");
        }
    }

    private static MapId RequireCurrentMapId(GameEngine engine, string configuredMapId)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("MassCrowd runtime requires an active map session before scenario bootstrap.");
        if (!string.Equals(session.MapId.Value, configuredMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MassCrowd runtime scenario bootstrap requires active map '{configuredMapId}', got '{session.MapId.Value}'.");
        }

        return session.MapId;
    }

}
