using System;

namespace Ludots.Core.Navigation.NavMesh.Surface
{
    /// <summary>
    /// Per-triangle surface semantics for layered-span clearance/slope filtering.
    /// Valid values are exactly <see cref="Solid"/> and <see cref="Solid"/>|<see cref="WalkCandidate"/>.
    /// </summary>
    [Flags]
    public enum NavTriangleSurfaceFlags : byte
    {
        Solid = 1 << 0,
        WalkCandidate = 1 << 1
    }
}
