using System;

namespace Ludots.Core.Navigation.NavMesh.LayeredSpan
{
    /// <summary>
    /// Explicit closed column-boundary coverage for a rasterized span.
    /// Bits are set only when the source triangle intersects that boundary segment.
    /// </summary>
    [Flags]
    public enum LayeredSpanBoundaryMask : byte
    {
        None = 0,
        West = 1 << 0,
        East = 1 << 1,
        North = 1 << 2,
        South = 1 << 3
    }
}
