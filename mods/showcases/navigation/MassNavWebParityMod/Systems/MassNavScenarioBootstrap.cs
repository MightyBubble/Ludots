using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod.Systems;

internal static class MassNavScenarioBootstrap
{
    public static void SpawnDefaultScenario(World world, MassNavSimulationRuntime simulation)
    {
        simulation.AgentState.Reset();
        TeamManager.DefaultRelationship = TeamRelationship.Hostile;
        TeamManager.SetRelationshipSymmetric(3, 4, TeamRelationship.Neutral);
        simulation.WebParity.Reset(simulation.TeamIds, simulation.AgentsPerTeam);

        for (int i = 0; i < simulation.WebParity.UnitCount; i++)
        {
            int teamId = simulation.WebParity.GetTeam(i);
            float xCm = simulation.WebParity.GetPositionX(i);
            float yCm = simulation.WebParity.GetPositionY(i);
            var worldPosition = Fix64Vec2.FromInt((int)xCm, (int)yCm);
            Entity entity = world.Create(
                new MassNavAgentTag(),
                new MassNavControllable(),
                new MassNavAgentIndex { Value = i },
                new Team { Id = teamId },
                new WorldPositionCm { Value = worldPosition },
                new PreviousWorldPositionCm { Value = worldPosition },
                new VisualTransform
                {
                    Position = new Vector3(xCm * MassNavWebParitySimState.VisualMetersPerCm, 0.25f, yCm * MassNavWebParitySimState.VisualMetersPerCm),
                    Scale = new Vector3(0.18f, 0.18f, 0.18f),
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
}
