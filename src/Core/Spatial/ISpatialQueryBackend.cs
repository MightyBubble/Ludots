using System;
using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Spatial
{
    public interface ISpatialQueryBackend
    {
        int QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer, out int dropped);

        int QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer, out int dropped);
    }
}
