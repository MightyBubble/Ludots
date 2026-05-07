using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal static class MassNavScenarioBootstrap
{
    public static void SpawnDefaultScenario(GameEngine engine, MassNavSimulationRuntime simulation, TeamEntityLookup teamLookup)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(teamLookup);

        MassNavAuthoringContract authoring = MassNavAuthoringContract.Require(engine, simulation.Config);
        RuntimeEntitySpawnQueue spawnQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue)
            ?? throw new InvalidOperationException("MassNavWebParityMod requires RuntimeEntitySpawnQueue.");
        RuntimeEntitySpawnReceiptQueue receiptQueue = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptQueue)
            ?? throw new InvalidOperationException("MassNavWebParityMod requires RuntimeEntitySpawnReceiptQueue.");
        int pendingMassNavReceipts = receiptQueue.CountForChannel(MassNavWebParityIds.RuntimeSpawnReceiptChannelId);
        if (pendingMassNavReceipts != 0)
        {
            throw new InvalidOperationException(
                $"MassNavWebParityMod requires its runtime spawn receipt channel to be empty before scenario bootstrap; pending={pendingMassNavReceipts}.");
        }

        simulation.AgentState.Reset();
        simulation.SpawnReceipts.Reset();
        ReadOnlySpan<int> teamIds = simulation.TeamIds;
        simulation.ConfigureScenarioTeams(teamIds);
        ConfigureRelationships(simulation.Config);
        simulation.WebParity.Reset(teamIds, simulation.AgentsPerTeam);

        for (int teamIndex = 0; teamIndex < teamIds.Length; teamIndex++)
        {
            int teamId = teamIds[teamIndex];
            string teamName = simulation.Config.Scenario.Teams[teamIndex].Name;
            teamLookup.Register(teamId, RelationshipTeamBootstrapper.EnsureTeamEntity(engine.World, teamLookup, teamId, teamName));
        }

        int requested = simulation.WebParity.UnitCount + simulation.WebParity.ObstacleCount + simulation.HotZones.Length;
        if (spawnQueue.FreeCapacity < requested)
        {
            throw new InvalidOperationException(
                $"MassNavWebParityMod requires RuntimeEntitySpawnQueue free capacity {requested}, actual {spawnQueue.FreeCapacity}.");
        }

        MapId mapId = engine.CurrentMapSession?.MapId ?? default;
        for (int i = 0; i < simulation.WebParity.UnitCount; i++)
        {
            int teamId = simulation.WebParity.GetTeam(i);
            float xCm = simulation.WebParity.GetPositionX(i);
            float yCm = simulation.WebParity.GetPositionY(i);
            float worldXCm = simulation.ToWorldXCm(xCm);
            float worldYCm = simulation.ToWorldYCm(yCm);
            float navMass = simulation.WebParity.GetNavMass(i);
            float visualScale = simulation.WebParity.GetVisualScale(i);
            bool heavy = MathF.Abs(visualScale - simulation.WebParity.AvoidanceTuning.HeavyVisualScale) < 0.001f;
            string templateId = simulation.Config.Presentation.ResolveAgentTemplateId(teamId, heavy);
            authoring.ValidateTemplate(templateId);
            EnqueueSpawn(
                spawnQueue,
                simulation,
                mapId,
                templateId,
                Fix64Vec2.FromInt((int)MathF.Round(worldXCm), (int)MathF.Round(worldYCm)),
                new MassNavSpawnReceiptBinding(
                    MassNavSpawnReceiptKind.Agent,
                    i,
                    teamId,
                    navMass,
                    visualScale,
                    blockerRadiusCm: 0f,
                    templateId));
        }

        for (int i = 0; i < simulation.WebParity.ObstacleCount; i++)
        {
            float xCm = simulation.WebParity.GetObstacleX(i);
            float yCm = simulation.WebParity.GetObstacleY(i);
            float worldXCm = simulation.ToWorldXCm(xCm);
            float worldYCm = simulation.ToWorldYCm(yCm);
            float radiusCm = simulation.WebParity.GetObstacleRadius(i);
            string templateId = simulation.Config.Presentation.BlockerTemplateId;
            authoring.ValidateTemplate(templateId);
            EnqueueSpawn(
                spawnQueue,
                simulation,
                mapId,
                templateId,
                Fix64Vec2.FromInt((int)MathF.Round(worldXCm), (int)MathF.Round(worldYCm)),
                new MassNavSpawnReceiptBinding(
                    MassNavSpawnReceiptKind.Blocker,
                    unitIndex: -1,
                    expectedTeamId: 0,
                    navMass: 0f,
                    visualScale: 0f,
                    radiusCm,
                    templateId));
        }

        string hotspotTemplateId = simulation.Config.Presentation.HotspotTemplateId;
        authoring.ValidateTemplate(hotspotTemplateId);
        ReadOnlySpan<MassNavHotZoneConfig> hotZones = simulation.HotZones;
        for (int i = 0; i < hotZones.Length; i++)
        {
            MassNavHotZoneConfig zone = hotZones[i];
            EnqueueSpawn(
                spawnQueue,
                simulation,
                mapId,
                hotspotTemplateId,
                Fix64Vec2.FromInt(zone.CenterXCm, zone.CenterYCm),
                new MassNavSpawnReceiptBinding(
                    MassNavSpawnReceiptKind.WorldMarker,
                    unitIndex: -1,
                    expectedTeamId: 0,
                    navMass: 0f,
                    visualScale: 0f,
                    blockerRadiusCm: 0f,
                    hotspotTemplateId));
        }

        simulation.MarkScenarioSpawned();
        simulation.MarkStructuralChange();
    }

    private static void ConfigureRelationships(MassNavWebParityConfig config)
    {
        TeamManager.LoadConfig(config.TeamRelationships);
    }

    private static void EnqueueSpawn(
        RuntimeEntitySpawnQueue spawnQueue,
        MassNavSimulationRuntime simulation,
        MapId mapId,
        string templateId,
        Fix64Vec2 worldPosition,
        in MassNavSpawnReceiptBinding binding)
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
            ReceiptChannelId = MassNavWebParityIds.RuntimeSpawnReceiptChannelId,
            ReceiptId = receiptId,
        };

        if (!spawnQueue.TryEnqueue(in request))
        {
            throw new InvalidOperationException("MassNavWebParityMod failed to enqueue runtime entity spawn request.");
        }
    }
}
