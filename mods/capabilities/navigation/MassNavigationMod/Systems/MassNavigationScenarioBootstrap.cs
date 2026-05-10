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
    public static void SpawnDefaultScenario(GameEngine engine, MassNavigationSimulationRuntime simulation, TeamEntityLookup teamLookup)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(teamLookup);

        MassNavigationAuthoringContract authoring = MassNavigationAuthoringContract.Require(engine, simulation.Config);
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("MassNavigationMod requires RuntimeEntitySpawnQueue.");
        RuntimeEntitySpawnReceiptQueue receiptQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue)
            ?? throw new InvalidOperationException("MassNavigationMod requires RuntimeEntitySpawnReceiptQueue.");
        int pendingMassNavigationReceipts = receiptQueue.CountForChannel(MassNavigationIds.RuntimeSpawnReceiptChannelId);
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
        simulation.MassFlow.Reset(teamIds, simulation.AgentsPerTeam, simulation.WorldConfig.Obstacles, simulation.Config.AgentProfiles);

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

        MapId mapId = engine.CurrentMapSession?.MapId ?? default;
        for (int i = 0; i < simulation.MassFlow.UnitCount; i++)
        {
            int teamId = simulation.MassFlow.GetTeam(i);
            float xCm = simulation.MassFlow.GetPositionX(i);
            float yCm = simulation.MassFlow.GetPositionY(i);
            float worldXCm = simulation.ToWorldXCm(xCm);
            float worldYCm = simulation.ToWorldYCm(yCm);
            float navMass = simulation.MassFlow.GetNavMass(i);
            float visualScale = simulation.MassFlow.GetVisualScale(i);
            bool heavy = simulation.MassFlow.IsHeavyProfile(i);
            string templateId = simulation.Config.Presentation.ResolveAgentTemplateId(teamId, heavy);
            authoring.ValidateTemplate(templateId);
            EnqueueSpawn(
                spawnQueue,
                simulation,
                mapId,
                templateId,
                Fix64Vec2.FromInt((int)MathF.Round(worldXCm), (int)MathF.Round(worldYCm)),
                new MassNavigationSpawnReceiptBinding(
                    MassNavigationSpawnReceiptKind.Agent,
                    i,
                    teamId,
                    heavy,
                    navMass,
                    visualScale,
                    blockerRadiusCm: 0f,
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
                templateId,
                Fix64Vec2.FromInt((int)MathF.Round(worldXCm), (int)MathF.Round(worldYCm)),
                new MassNavigationSpawnReceiptBinding(
                    MassNavigationSpawnReceiptKind.Blocker,
                    unitIndex: -1,
                    expectedTeamId: 0,
                    heavy: false,
                    navMass: 0f,
                    visualScale: 0f,
                    radiusCm,
                    templateId));
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
                hotspotTemplateId,
                Fix64Vec2.FromInt(zone.CenterXCm, zone.CenterYCm),
                new MassNavigationSpawnReceiptBinding(
                    MassNavigationSpawnReceiptKind.WorldMarker,
                    unitIndex: -1,
                    expectedTeamId: 0,
                    heavy: false,
                    navMass: 0f,
                    visualScale: 0f,
                    blockerRadiusCm: 0f,
                    hotspotTemplateId));
        }

        simulation.MarkScenarioSpawned();
        simulation.MarkStructuralChange();
    }

    private static void ConfigureRelationships(MassNavigationConfig config)
    {
        TeamManager.LoadConfig(config.TeamRelationships);
    }

    private static void EnqueueSpawn(
        RuntimeEntitySpawnQueue spawnQueue,
        MassNavigationSimulationRuntime simulation,
        MapId mapId,
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
            ReceiptChannelId = MassNavigationIds.RuntimeSpawnReceiptChannelId,
            ReceiptId = receiptId,
        };

        if (!spawnQueue.TryEnqueue(in request))
        {
            throw new InvalidOperationException("MassNavigationMod failed to enqueue runtime entity spawn request.");
        }
    }
}


