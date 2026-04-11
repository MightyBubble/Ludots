using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal static class MassNavScenarioBootstrap
{
    public static void SpawnDefaultScenario(World world, MassNavSimulationRuntime simulation, TeamEntityLookup teamLookup)
    {
        if (teamLookup == null)
        {
            throw new ArgumentNullException(nameof(teamLookup));
        }

        simulation.AgentState.Reset();
        ReadOnlySpan<int> teamIds = simulation.TeamIds;
        simulation.ConfigureScenarioTeams(teamIds);
        ConfigureRelationships(simulation.Config);
        simulation.WebParity.Reset(teamIds, simulation.AgentsPerTeam);

        for (int teamIndex = 0; teamIndex < teamIds.Length; teamIndex++)
        {
            int teamId = teamIds[teamIndex];
            string teamName = simulation.Config.Scenario.Teams[teamIndex].Name;
            teamLookup.Register(teamId, RelationshipTeamBootstrapper.EnsureTeamEntity(world, teamLookup, teamId, teamName));
        }

        for (int i = 0; i < simulation.WebParity.UnitCount; i++)
        {
            int teamId = simulation.WebParity.GetTeam(i);
            float xCm = simulation.WebParity.GetPositionX(i);
            float yCm = simulation.WebParity.GetPositionY(i);
            float navMass = simulation.WebParity.GetNavMass(i);
            float visualScale = simulation.WebParity.GetVisualScale(i);
            var worldPosition = Fix64Vec2.FromInt((int)xCm, (int)yCm);
            Entity entity = world.Create(
                new MassNavAgentTag(),
                new MassNavControllable(),
                new MassNavAgentIndex { Value = i },
                new MassNavAgentProfile { NavMass = navMass, VisualScale = visualScale },
                new Team { Id = teamId },
                OrderBuffer.CreateEmpty(),
                new WorldPositionCm { Value = worldPosition },
                new PreviousWorldPositionCm { Value = worldPosition },
                new VisualTransform
                {
                    Position = new Vector3(xCm * MassNavWebParitySimState.VisualMetersPerCm, 0.25f, yCm * MassNavWebParitySimState.VisualMetersPerCm),
                    Scale = new Vector3(visualScale, visualScale, visualScale),
                    Rotation = Quaternion.Identity,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High },
                default(SelectionSelectableTag));
            simulation.AgentState.RegisterAgent(entity, controllable: true);
        }

        for (int i = 0; i < simulation.WebParity.ObstacleCount; i++)
        {
            float xCm = simulation.WebParity.GetObstacleX(i);
            float yCm = simulation.WebParity.GetObstacleY(i);
            float radiusCm = simulation.WebParity.GetObstacleRadius(i);
            var worldPosition = Fix64Vec2.FromInt((int)xCm, (int)yCm);
            Entity entity = world.Create(
                new MassNavBlocker(),
                new WorldPositionCm { Value = worldPosition },
                new PreviousWorldPositionCm { Value = worldPosition },
                new VisualTransform
                {
                    Position = new Vector3(xCm * MassNavWebParitySimState.VisualMetersPerCm, 0.15f, yCm * MassNavWebParitySimState.VisualMetersPerCm),
                    Scale = new Vector3(radiusCm * MassNavWebParitySimState.VisualMetersPerCm * 2f, 0.3f, radiusCm * MassNavWebParitySimState.VisualMetersPerCm * 2f),
                    Rotation = Quaternion.Identity,
                },
                new CullState { IsVisible = true, LOD = LODLevel.High });
            simulation.AgentState.RegisterBlocker(entity);
        }
    }

    private static void ConfigureRelationships(MassNavWebParityConfig config)
    {
        TeamManager.LoadConfig(config.TeamRelationships);
    }
}
