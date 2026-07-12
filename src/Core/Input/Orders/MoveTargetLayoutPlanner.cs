using System;
using System.Numerics;

namespace Ludots.Core.Input.Orders
{
    internal static class MoveTargetLayoutPlanner
    {
        public static Vector3 ComputeOffsetTarget(Vector3 anchorWorldCm, int index, int totalCount, int spacingCm)
        {
            if (totalCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "Target layout requires totalCount > 0.");
            }

            if ((uint)index >= (uint)totalCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Target layout index must reference an actor in the requested layout.");
            }

            if (spacingCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spacingCm), spacingCm, "Target layout requires spacingCm > 0.");
            }

            if (totalCount == 1)
            {
                return anchorWorldCm;
            }

            GetGridLayout(totalCount, out int cols, out int rows);
            GetGridCell(index, cols, out int row, out int col);

            float offsetX = GetCenteredOffset(col, cols, spacingCm);
            float offsetZ = GetCenteredOffset(row, rows, spacingCm);
            return new Vector3(anchorWorldCm.X + offsetX, anchorWorldCm.Y, anchorWorldCm.Z + offsetZ);
        }

        private static void GetGridLayout(int count, out int cols, out int rows)
        {
            cols = (int)Math.Ceiling(Math.Sqrt(count));
            rows = (int)Math.Ceiling(count / (double)cols);
        }

        private static void GetGridCell(int index, int cols, out int row, out int col)
        {
            row = index / cols;
            col = index % cols;
        }

        private static int GetCenteredOffset(int index, int count, int spacingCm)
        {
            return -((count - 1) * spacingCm / 2) + index * spacingCm;
        }
    }
}
