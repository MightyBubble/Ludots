using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;

namespace MassNavWebParityMod.Runtime;

public static class MassNavCommandRuntime
{
    public static int ApplyGridMove(World world, ReadOnlySpan<Entity> selected, Vector2 centerCm, int spacingCm, int goalRadiusCm)
    {
        GetGridLayout(selected.Length, out int cols, out int rows);
        int assigned = 0;
        for (int i = 0; i < selected.Length; i++)
        {
            Entity entity = selected[i];
            if (!world.IsAlive(entity) || !world.Has<NavGoal2D>(entity))
            {
                continue;
            }

            GetGridCell(assigned, cols, out int row, out int col);
            Vector2 offset = new(
                GetCenteredOffset(col, cols, spacingCm),
                GetCenteredOffset(row, rows, spacingCm));
            ref var goal = ref world.Get<NavGoal2D>(entity);
            goal.Kind = NavGoalKind2D.Point;
            goal.TargetCm = Fix64Vec2.FromInt(
                (int)MathF.Round(centerCm.X + offset.X),
                (int)MathF.Round(centerCm.Y + offset.Y));
            goal.RadiusCm = Fix64.FromInt(goalRadiusCm);
            assigned++;
        }

        return assigned;
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
