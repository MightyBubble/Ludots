using System;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

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

        Vector3 LogicToVisual(IntVector2 logicPos, int heightLevel);

        IntVector2 VisualToLogic(Vector3 visualPos);

        /// <summary>
        /// Designed for 0GC (Zero Garbage Collection) high-performance scenarios using Spans.
        /// </summary>
        /// <param name="heights">Optional; treated as 0 when empty or shorter.</param>
        /// <param name="visualPositions">Must be at least as large as logicPositions.</param>
        void BatchLogicToVisual(
            ReadOnlySpan<IntVector2> logicPositions, 
            ReadOnlySpan<int> heights, 
            Span<Vector3> visualPositions
        );
    }
}
