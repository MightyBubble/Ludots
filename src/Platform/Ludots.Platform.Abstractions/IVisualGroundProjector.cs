using System;

namespace Ludots.Platform.Abstractions
{
    /// <summary>
    /// Projects world XY positions in centimeters onto the visual ground surface.
    /// Implementations may use platform-native terrain data, height fields, or scene adapters.
    /// </summary>
    public interface IVisualGroundProjector
    {
        /// <summary>
        /// Projects world XY positions to height values in centimeters.
        /// Returns false when the projector is temporarily unavailable.
        /// </summary>
        bool TryProjectHeights(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm);
    }
}
