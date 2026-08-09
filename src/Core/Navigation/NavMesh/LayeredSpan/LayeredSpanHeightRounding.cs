namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Explicit integer rounding for rational plane-height evaluation during triangulation.
    /// No default: callers must pass a concrete policy through LayeredSpanTriangulationSpec.
    /// </summary>
    public enum LayeredSpanHeightRounding : byte
    {
        /// <summary>Floor toward negative infinity (Euclidean quotient for positive denominator).</summary>
        FloorTowardNegativeInfinity = 0,

        /// <summary>
        /// Round half away from zero: |r| &gt;= |den|/2 rounds away from zero; ties on exact half also away from zero.
        /// </summary>
        RoundHalfAwayFromZero = 1
    }
}
