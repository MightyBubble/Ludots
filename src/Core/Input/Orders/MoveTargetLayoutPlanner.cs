using System;
using System.Numerics;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Input.Orders
{
    internal static class MoveTargetLayoutPlanner
    {
        private const int IntegerMovementSafetyMarginCm = 1;

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

            WorldCmInt2 offset = ComputeOffsetCm(index, totalCount, spacingCm);
            return new Vector3(anchorWorldCm.X + offset.X, anchorWorldCm.Y, anchorWorldCm.Z + offset.Y);
        }

        public static bool TryComputePositionPreservingSlots(
            ReadOnlySpan<WorldCmInt2> actorWorldCm,
            Vector3 anchorWorldCm,
            int spacingCm,
            Span<int> slotByActor,
            Span<int> actorIndicesScratch,
            Span<int> slotIndicesScratch,
            Span<Int128> actorForwardScratch,
            Span<Int128> actorLateralScratch,
            Span<Int128> slotForwardScratch,
            Span<Int128> slotLateralScratch)
        {
            if (actorWorldCm.IsEmpty)
            {
                throw new ArgumentOutOfRangeException(nameof(actorWorldCm), "Relative-order target layout requires at least one actor.");
            }

            int count = actorWorldCm.Length;
            RequireScratchLength(slotByActor, count, nameof(slotByActor));
            RequireScratchLength(actorIndicesScratch, count, nameof(actorIndicesScratch));
            RequireScratchLength(slotIndicesScratch, count, nameof(slotIndicesScratch));
            RequireScratchLength(actorForwardScratch, count, nameof(actorForwardScratch));
            RequireScratchLength(actorLateralScratch, count, nameof(actorLateralScratch));
            RequireScratchLength(slotForwardScratch, count, nameof(slotForwardScratch));
            RequireScratchLength(slotLateralScratch, count, nameof(slotLateralScratch));
            if (spacingCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spacingCm), spacingCm, "Relative-order target layout requires spacingCm > 0.");
            }

            WorldCmInt2 anchor = ToWorldCmInt2(anchorWorldCm);
            Int128 sumX = 0;
            Int128 sumY = 0;
            for (int i = 0; i < count; i++)
            {
                sumX += actorWorldCm[i].X;
                sumY += actorWorldCm[i].Y;
            }

            Int128 count128 = count;
            Int128 forwardX = checked(((Int128)anchor.X * count128) - sumX);
            Int128 forwardY = checked(((Int128)anchor.Y * count128) - sumY);
            if (forwardX == 0 && forwardY == 0)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                Int128 relativeX = checked(((Int128)actorWorldCm[i].X * count128) - sumX);
                Int128 relativeY = checked(((Int128)actorWorldCm[i].Y * count128) - sumY);
                Project(
                    relativeX,
                    relativeY,
                    forwardX,
                    forwardY,
                    out actorForwardScratch[i],
                    out actorLateralScratch[i]);

                WorldCmInt2 offset = ComputeOffsetCm(i, count, spacingCm);
                Project(
                    offset.X,
                    offset.Y,
                    forwardX,
                    forwardY,
                    out slotForwardScratch[i],
                    out slotLateralScratch[i]);
            }

            Span<int> actorIndices = actorIndicesScratch[..count];
            Span<int> slotIndices = slotIndicesScratch[..count];
            SortIndices(actorIndices, actorForwardScratch[..count], actorLateralScratch[..count]);
            SortIndices(slotIndices, slotForwardScratch[..count], slotLateralScratch[..count]);
            for (int rank = 0; rank < count; rank++)
            {
                slotByActor[actorIndices[rank]] = slotIndices[rank];
            }

            return true;
        }

        internal static WorldCmInt2 ComputeOffsetCm(int index, int totalCount, int spacingCm)
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

            int slotSeparationCm = GetSlotSeparationCm(spacingCm);
            GetGridLayout(totalCount, out int cols, out int rows);
            GetGridCell(index, cols, out int row, out int col);
            return new WorldCmInt2(
                GetCenteredOffset(col, cols, slotSeparationCm),
                GetCenteredOffset(row, rows, slotSeparationCm));
        }

        private static int GetSlotSeparationCm(int spacingCm)
        {
            if (spacingCm == int.MaxValue)
            {
                throw new InvalidOperationException(
                    "Target layout spacing leaves no room for the integer movement safety margin.");
            }

            return spacingCm + IntegerMovementSafetyMarginCm;
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
            long first = -checked(((long)count - 1L) * spacingCm / 2L);
            long offset = checked(first + ((long)index * spacingCm));
            if (offset < int.MinValue || offset > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Target layout offset {offset}cm exceeds the supported signed 32-bit world-centimeter range.");
            }

            return (int)offset;
        }

        private static WorldCmInt2 ToWorldCmInt2(Vector3 position)
        {
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Z))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(position),
                    "Relative-order target layout anchor must contain finite X and Z values.");
            }

            double roundedX = Math.Round((double)position.X, MidpointRounding.AwayFromZero);
            double roundedY = Math.Round((double)position.Z, MidpointRounding.AwayFromZero);
            if (roundedX < int.MinValue || roundedX > int.MaxValue ||
                roundedY < int.MinValue || roundedY > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(position),
                    "Relative-order target layout anchor exceeds the supported signed 32-bit world-centimeter range.");
            }

            return new WorldCmInt2((int)roundedX, (int)roundedY);
        }

        private static void Project(
            Int128 relativeX,
            Int128 relativeY,
            Int128 forwardX,
            Int128 forwardY,
            out Int128 forward,
            out Int128 lateral)
        {
            forward = checked((relativeX * forwardX) + (relativeY * forwardY));
            lateral = checked((relativeX * -forwardY) + (relativeY * forwardX));
        }

        private static void SortIndices(
            Span<int> indices,
            ReadOnlySpan<Int128> forward,
            ReadOnlySpan<Int128> lateral)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            for (int start = (indices.Length / 2) - 1; start >= 0; start--)
            {
                SiftDown(indices, forward, lateral, start, indices.Length);
            }

            for (int end = indices.Length - 1; end > 0; end--)
            {
                (indices[0], indices[end]) = (indices[end], indices[0]);
                SiftDown(indices, forward, lateral, 0, end);
            }
        }

        private static void SiftDown(
            Span<int> indices,
            ReadOnlySpan<Int128> forward,
            ReadOnlySpan<Int128> lateral,
            int root,
            int count)
        {
            while (true)
            {
                int child = (root * 2) + 1;
                if (child >= count)
                {
                    return;
                }

                int swap = root;
                if (Compare(indices[swap], indices[child], forward, lateral) < 0)
                {
                    swap = child;
                }

                int right = child + 1;
                if (right < count && Compare(indices[swap], indices[right], forward, lateral) < 0)
                {
                    swap = right;
                }

                if (swap == root)
                {
                    return;
                }

                (indices[root], indices[swap]) = (indices[swap], indices[root]);
                root = swap;
            }
        }

        private static int Compare(
            int left,
            int right,
            ReadOnlySpan<Int128> forward,
            ReadOnlySpan<Int128> lateral)
        {
            int byForward = forward[left].CompareTo(forward[right]);
            if (byForward != 0)
            {
                return byForward;
            }

            int byLateral = lateral[left].CompareTo(lateral[right]);
            return byLateral != 0 ? byLateral : left.CompareTo(right);
        }

        private static void RequireScratchLength<T>(Span<T> scratch, int required, string name)
        {
            if (scratch.Length < required)
            {
                throw new ArgumentException(
                    $"Relative-order target layout requires {name} capacity {required}, actual {scratch.Length}.",
                    name);
            }
        }
    }
}
