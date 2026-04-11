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
    public static void SpawnDefaultScenario(World world, MassNavSimulationRuntime simulation, TeamEntityLookup? teamLookup = null)
    {
        simulation.AgentState.Reset();
        int[] teamIds = ResolveScenarioTeams(world, simulation, teamLookup);
        simulation.ConfigureScenarioTeams(teamIds);
        ConfigureRelationships(teamIds);
        simulation.WebParity.Reset(teamIds, simulation.AgentsPerTeam);

        for (int teamIndex = 0; teamIndex < teamIds.Length; teamIndex++)
        {
            int teamId = teamIds[teamIndex];
            teamLookup?.Register(teamId, RelationshipTeamBootstrapper.EnsureTeamEntity(world, teamLookup, teamId, $"MassNav Team {teamId}"));
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

    private static int[] ResolveScenarioTeams(World world, MassNavSimulationRuntime simulation, TeamEntityLookup? teamLookup)
    {
        if (teamLookup != null && teamLookup.Count > 0)
        {
            var teamIds = new int[teamLookup.Count];
            int index = 0;
            foreach (var entry in teamLookup.Entries)
            {
                teamIds[index++] = entry.Key;
            }

            System.Array.Sort(teamIds);
            return teamIds;
        }

        if (simulation.TeamCount > 0)
        {
            return simulation.TeamIds.ToArray();
        }

        return new[] { 1, 2, 3, 4 };
    }

    private static void ConfigureRelationships(ReadOnlySpan<int> teamIds)
    {
        TeamManager.Clear();
        TeamManager.DefaultRelationship = TeamRelationship.Hostile;
        if (teamIds.Length >= 4)
        {
            TeamManager.SetRelationshipSymmetric(teamIds[2], teamIds[3], TeamRelationship.Neutral);
        }
    }
}
