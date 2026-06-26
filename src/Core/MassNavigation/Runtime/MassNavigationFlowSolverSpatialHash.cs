using System;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed partial class MassNavigationFlowSolverState
{
    private void BuildSeparationHash(float[] positionsCm)
    {
        BuildBucketHash(
            positionsCm,
            _separationHashWidth,
            _separationHashHeight,
            1f / _separationHashCellSizeCm,
            _separationCellCounts,
            _separationCellOffsets,
            _separationCellCursor,
            _separationAgents);
    }

    private void BuildHardResolveHash(float[] positionsCm)
    {
        BuildBucketHash(
            positionsCm,
            _hardResolveHashWidth,
            _hardResolveHashHeight,
            1f / _hardResolveHashCellSizeCm,
            _hardResolveCellCounts,
            _hardResolveCellOffsets,
            _hardResolveCellCursor,
            _hardResolveAgents);
    }

    private void BuildBucketHash(
        float[] positionsCm,
        int hashWidth,
        int hashHeight,
        float invHashCell,
        int[] cellCounts,
        int[] cellOffsets,
        int[] cellCursor,
        int[] cellAgents)
    {
        Array.Clear(cellCounts, 0, cellCounts.Length);
        int hashWidthMinusOne = hashWidth - 1;
        int hashHeightMinusOne = hashHeight - 1;

        for (int i = 0; i < UnitCount; i++)
        {
            if (TryGetHashCell(positionsCm, i, invHashCell, hashWidthMinusOne, hashHeightMinusOne, hashWidth, out int cell))
            {
                cellCounts[cell]++;
            }
        }

        int offset = 0;
        for (int cell = 0; cell < cellCounts.Length; cell++)
        {
            cellOffsets[cell] = offset;
            cellCursor[cell] = offset;
            offset += cellCounts[cell];
        }

        for (int i = 0; i < UnitCount; i++)
        {
            if (TryGetHashCell(positionsCm, i, invHashCell, hashWidthMinusOne, hashHeightMinusOne, hashWidth, out int cell))
            {
                cellAgents[cellCursor[cell]++] = i;
            }
        }
    }

    private static bool TryGetHashCell(
        float[] positionsCm,
        int index,
        float invHashCell,
        int hashWidthMinusOne,
        int hashHeightMinusOne,
        int hashWidth,
        out int cell)
    {
        int i2 = index << 1;
        int cellX = (int)(positionsCm[i2] * invHashCell);
        int cellY = (int)(positionsCm[i2 + 1] * invHashCell);
        if ((uint)cellX > (uint)hashWidthMinusOne || (uint)cellY > (uint)hashHeightMinusOne)
        {
            cell = -1;
            return false;
        }

        cell = (cellY * hashWidth) + cellX;
        return true;
    }
}
