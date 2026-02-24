using System;
using System.Numerics;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Presentation
{
    /// <summary>
    /// Defines the strategy for mapping logical grid coordinates to visual 3D coordinates.
    /// Implementations handle differences between coordinate systems (e.g., Unity Y-up vs Unreal Z-up).
    /// </summary>
    public interface ICoordinateMapper
    {
        /// <summary>
        /// Gets the platform-specific scale factor (e.g., 0.001f for 1000 fixed-point -> 1.0f).
        /// </summary>
        float ScaleFactor { get; }

        /// <summary>
        /// Converts a single logical position to visual space.
        /// </summary>
        Vector3 LogicToVisual(IntVector2 logicPos, int heightLevel);

        /// <summary>
        /// Converts a visual position back to logical grid space.
        /// </summary>
        IntVector2 VisualToLogic(Vector3 visualPos);

        /// <summary>
        /// Batch converts logical positions to visual positions.
        /// Designed for 0GC (Zero Garbage Collection) high-performance scenarios using Spans.
        /// </summary>
        /// <param name="logicPositions">Source logical positions.</param>
        /// <param name="heights">Source height levels (optional, can be treated as 0 if empty/shorter).</param>
        /// <param name="visualPositions">Destination buffer for visual positions. Must be at least as large as logicPositions.</param>
        void BatchLogicToVisual(
            ReadOnlySpan<IntVector2> logicPositions, 
            ReadOnlySpan<int> heights, 
            Span<Vector3> visualPositions
        );
    }
}
