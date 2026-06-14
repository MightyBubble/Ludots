using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using MassNavigationMod.Runtime;

namespace MassNavigationMod.Systems;

internal static class MassNavigationScenarioBootstrap
{
    public static void SpawnConfiguredScenario(GameEngine engine, MassNavigationSimulationRuntime simulation, TeamEntityLookup teamLookup)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(teamLookup);

        MassNavigationAuthoringContract authoring = MassNavigationAuthoringContract.Require(engine, simulation.Config);
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("MassNavigationMod requires RuntimeEntitySpawnQueue.");
        RuntimeEntitySpawnReceiptQueue receiptQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue)
            ?? throw new InvalidOperationException("MassNavigationMod requires RuntimeEntitySpawnReceiptQueue.");
        int receiptChannelId = ResolveReceiptChannelId(engine);
        int pendingMassNavigationRequests = spawnQueue.CountForReceiptChannel(receiptChannelId);
        if (pendingMassNavigationRequests != 0)
        {
            throw new InvalidOperationException(
                $"MassNavigationMod requires its runtime spawn request channel to be empty before scenario bootstrap; pending={pendingMassNavigationRequests}.");
        }

        int pendingMassNavigationReceipts = receiptQueue.CountForChannel(receiptChannelId);
        if (pendingMassNavigationReceipts != 0)
        {
            throw new InvalidOperationException(
                $"MassNavigationMod requires its runtime spawn receipt channel to be empty before scenario bootstrap; pending={pendingMassNavigationReceipts}.");
        }

        simulation.AgentState.Reset();
        simulation.SpawnReceipts.Reset();
        ReadOnlySpan<int> teamIds = simulation.TeamIds;
        simulation.ConfigureScenarioTeams(teamIds);
        ConfigureRelationships(simulation.Config);
        MassNavigationAgentLayer scenarioAgentLayer = ResolveScenarioAgentLayer(authoring, simulation.Config);
        simulation.MassFlow.Reset(
            teamIds,
            simulation.AgentsPerTeam,
            simulation.WorldConfig.Obstacles,
            simulation.Config.AgentProfiles,
            scenarioAgentLayer,
            simulation.Config.Scenario.SpawnLayout);

        for (int teamIndex = 0; teamIndex < teamIds.Length; teamIndex++)
        {
            int teamId = teamIds[teamIndex];
            string teamName = simulation.Config.Scenario.Teams[teamIndex].Name;
            teamLookup.Register(teamId, RelationshipTeamBootstrapper.EnsureTeamEntity(engine.World, teamLookup, teamId, teamName));
        }

        int requested = simulation.MassFlow.UnitCount + simulation.MassFlow.ObstacleCount + simulation.HotZones.Length;
        if (spawnQueue.FreeCapacity < requested)
        {
            throw new InvalidOperationException(
                $"MassNavigationMod requires RuntimeEntitySpawnQueue free capacity {requested}, actual {spawnQueue.FreeCapacity}.");
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
                simulation,
                mapId,
                receiptChannelId,
                templateId,
                Fix64Vec2.FromInt((int)MathF.Round(worldXCm), (int)MathF.Round(worldYCm)),
                MassNavigationSpawnReceiptBinding.ForAgent(
                    agentIndex: i,
                    expectedTeamId: teamId,
                    heavy,
                    navMass,
                    visualScale,
                    bodyRadiusCm,
                    speedCmPerSecond,
                    templateId));
        }

        for (int i = 0; i < simulation.MassFlow.ObstacleCount; i++)
        {
            float xCm = simulation.MassFlow.GetObstacleX(i);
            float yCm = simulation.MassFlow.GetObstacleY(i);
            float worldXCm = simulation.ToWorldXCm(xCm);
            float worldYCm = simulation.ToWorldYCm(yCm);
            float radiusCm = simulation.MassFlow.GetObstacleRadius(i);
            string templateId = simulation.Config.Presentation.BlockerTemplateId;
            authoring.ValidateTemplate(templateId);
            EnqueueSpawn(
                spawnQueue,
                simulation,
                mapId,
                receiptChannelId,
                templateId,
                Fix64Vec2.FromInt((int)MathF.Round(worldXCm), (int)MathF.Round(worldYCm)),
                MassNavigationSpawnReceiptBinding.ForBlocker(radiusCm, templateId));
        }

        string hotspotTemplateId = simulation.Config.Presentation.HotspotTemplateId;
        authoring.ValidateTemplate(hotspotTemplateId);
        ReadOnlySpan<MassNavigationHotZoneConfig> hotZones = simulation.HotZones;
        for (int i = 0; i < hotZones.Length; i++)
        {
            MassNavigationHotZoneConfig zone = hotZones[i];
            EnqueueSpawn(
                spawnQueue,
                simulation,
                mapId,
                receiptChannelId,
                hotspotTemplateId,
                Fix64Vec2.FromInt(zone.CenterXCm, zone.CenterYCm),
                MassNavigationSpawnReceiptBinding.ForWorldMarker(hotspotTemplateId));
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

        return resolved ?? throw new InvalidOperationException("MassNavigationMod requires at least one configured presentation team.");
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
            $"MassNavigationMod scenario auto-spawn requires one explicit agent layer across generated agent templates; '{actualLabel}' differs from '{expectedLabel}'.");
    }

    private static void EnqueueSpawn(
        RuntimeEntitySpawnQueue spawnQueue,
        MassNavigationSimulationRuntime simulation,
        MapId mapId,
        int receiptChannelId,
        string templateId,
        Fix64Vec2 worldPosition,
        in MassNavigationSpawnReceiptBinding binding)
    {
        int receiptId = simulation.SpawnReceipts.Allocate(in binding);
        var request = new RuntimeEntitySpawnRequest
        {
            Kind = RuntimeEntitySpawnKind.Template,
            TemplateId = templateId,
            MapId = mapId,
            WorldPositionCm = worldPosition,
            HasWorldPosition = 1,
            EmitReceipt = 1,
            ReceiptChannelId = receiptChannelId,
            ReceiptId = receiptId,
        };

        if (!spawnQueue.TryEnqueue(in request))
        {
            throw new InvalidOperationException("MassNavigationMod failed to enqueue runtime entity spawn request.");
        }
    }

    private static int ResolveReceiptChannelId(GameEngine engine)
    {
        RuntimeEntitySpawnReceiptChannelRegistry channels = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry)
            ?? throw new InvalidOperationException("MassNavigationMod requires RuntimeEntitySpawnReceiptChannelRegistry.");
        return channels.Register(MassNavigationIds.RuntimeSpawnReceiptChannelKey);
    }

    private static MapId RequireCurrentMapId(GameEngine engine, string configuredMapId)
    {
        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("MassNavigationMod requires an active map session before scenario bootstrap.");
        if (!string.Equals(session.MapId.Value, configuredMapId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MassNavigationMod scenario bootstrap requires active map '{configuredMapId}', got '{session.MapId.Value}'.");
        }

        return session.MapId;
    }
}


