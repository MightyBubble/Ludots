using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Input.Selection;
using MassNavPlaygroundMod.Runtime;

namespace MassNavPlaygroundMod.Systems;

internal static class MassNavScenarioBootstrap
{
    public static void SpawnDefaultScenario(World world, MassNavAgentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Reset();

        const int totalPerTeam = 10_000;
        const int spacingCm = 120;
        const int startOffsetCm = 9000;
        const int goalOffsetCm = 9000;
        const int goalRadiusCm = 120;

        GetGridLayout(totalPerTeam, out int cols, out int rows);
        for (int index = 0; index < totalPerTeam; index++)
        {
            GetGridCell(index, cols, out int row, out int col);
            int laneY = GetCenteredOffset(row, rows, spacingCm);
            int depth = startOffsetCm + col * spacingCm;
            SpawnAgent(world, state, 0, new Vector2(-depth, laneY), new Vector2(goalOffsetCm, laneY), goalRadiusCm, controllable: true);
            SpawnAgent(world, state, 1, new Vector2(depth, laneY), new Vector2(-goalOffsetCm, laneY), goalRadiusCm, controllable: false);
        }

        SpawnBlocker(world, state, 0, 520, 180);
        SpawnBlocker(world, state, 0, -520, 180);
        SpawnBlocker(world, state, 0, 980, 180);
        SpawnBlocker(world, state, 0, -980, 180);
    }

    private static void SpawnAgent(World world, MassNavAgentState state, byte teamId, Vector2 start, Vector2 goal, int goalRadiusCm, bool controllable)
    {
        var kinematics = new NavKinematics2D
        {
            MaxSpeedCmPerSec = Fix64.FromInt(800),
            MaxAccelCmPerSec2 = Fix64.FromInt(6000),
            RadiusCm = Fix64.FromInt(40),
            NeighborDistCm = Fix64.FromInt(400),
            TimeHorizonSec = Fix64.FromInt(2),
            MaxNeighbors = 16,
        };

        var position = Fix64Vec2.FromVector2(start);
        var goalPosition = Fix64Vec2.FromVector2(goal);
        Entity entity;
        if (controllable)
        {
            entity = world.Create(
                new MassNavAgentTag(),
                new MassNavControllable(),
                new NavAgent2D(),
                new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = goalPosition, RadiusCm = Fix64.FromInt(goalRadiusCm) },
                kinematics,
                new Position2D { Value = position },
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new WorldPositionCm { Value = position },
                new PreviousWorldPositionCm { Value = position },
                VisualTransform.Default,
                new CullState { IsVisible = true, LOD = LODLevel.High },
                new MassNavTeam { Id = teamId },
                default(SelectionSelectableTag));
        }
        else
        {
            entity = world.Create(
                new MassNavAgentTag(),
                new NavAgent2D(),
                new NavGoal2D { Kind = NavGoalKind2D.Point, TargetCm = goalPosition, RadiusCm = Fix64.FromInt(goalRadiusCm) },
                kinematics,
                new Position2D { Value = position },
                Velocity2D.Zero,
                Mass2D.FromFloat(1f, 1f),
                new WorldPositionCm { Value = position },
                new PreviousWorldPositionCm { Value = position },
                VisualTransform.Default,
                new CullState { IsVisible = true, LOD = LODLevel.High },
                new MassNavTeam { Id = teamId });
        }

        state.RegisterAgent(entity, controllable);
    }

    private static void SpawnBlocker(World world, MassNavAgentState state, int x, int y, int radiusCm)
    {
        var position = Fix64Vec2.FromInt(x, y);
        world.Create(
            new MassNavBlocker(),
            new NavAgent2D(),
            new NavObstacle2D(),
            new NavKinematics2D
            {
                MaxSpeedCmPerSec = Fix64.Zero,
                MaxAccelCmPerSec2 = Fix64.Zero,
                RadiusCm = Fix64.FromInt(radiusCm),
                NeighborDistCm = Fix64.Zero,
                TimeHorizonSec = Fix64.OneValue,
                MaxNeighbors = 0,
            },
            new Position2D { Value = position },
            Velocity2D.Zero,
            Mass2D.Static,
            new WorldPositionCm { Value = position },
            new PreviousWorldPositionCm { Value = position },
            VisualTransform.Default,
            new CullState { IsVisible = true, LOD = LODLevel.High },
            new MassNavTeam { Id = byte.MaxValue });
        state.RegisterBlocker();
    }

    private static void GetGridLayout(int count, out int cols, out int rows)
    {
        cols = count <= 0 ? 0 : (int)Math.Ceiling(Math.Sqrt(count));
        rows = cols <= 0 ? 0 : (int)Math.Ceiling(count / (double)cols);
    }

    private static void GetGridCell(int index, int cols, out int row, out int col)
    {
        row = cols <= 0 ? 0 : index / cols;
        col = cols <= 0 ? 0 : index % cols;
    }

    private static int GetCenteredOffset(int index, int count, int spacingCm)
    {
        return count <= 0 ? 0 : -((count - 1) * spacingCm / 2) + index * spacingCm;
    }
}
