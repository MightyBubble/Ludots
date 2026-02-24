using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Presentation.Coordinates
{
    /// <summary>
    /// Coordinate mapper for Unity (Left-handed, Y-Up).
    /// Maps Logic Y -> Unity Z.
    /// </summary>
    public class UnityCoordinateMapper : ICoordinateMapper
    {
        public float ScaleFactor => 0.001f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 LogicToVisual(IntVector2 logicPos, int heightLevel)
        {
            return new Vector3(
                logicPos.X * ScaleFactor,
                heightLevel * ScaleFactor,
                logicPos.Y * ScaleFactor
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IntVector2 VisualToLogic(Vector3 visualPos)
        {
            return new IntVector2(
                (int)(visualPos.X / ScaleFactor),
                (int)(visualPos.Z / ScaleFactor)
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void BatchLogicToVisual(
            ReadOnlySpan<IntVector2> logicPositions, 
            ReadOnlySpan<int> heights, 
            Span<Vector3> visualPositions)
        {
            int count = Math.Min(logicPositions.Length, visualPositions.Length);
            bool hasHeights = heights.Length >= count;

            for (int i = 0; i < count; i++)
            {
                float h = hasHeights ? heights[i] * ScaleFactor : 0f;
                visualPositions[i] = new Vector3(
                    logicPositions[i].X * ScaleFactor,
                    h,
                    logicPositions[i].Y * ScaleFactor
                );
            }
        }
    }
}
