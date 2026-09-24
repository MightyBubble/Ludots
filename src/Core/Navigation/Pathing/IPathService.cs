using System;

namespace Ludots.Core.Navigation.Pathing
{
    public interface IPathService
    {
        bool TrySolve(in PathRequest request, out PathResult result);
        bool TryCopyPath(in PathHandle handle, Span<int> xcmOut, Span<int> ycmOut, out int count);

        /// <summary>
        /// Constrains a desired world position to the walkable navigation surface for the given
        /// agent type, the corridor-correction primitive. A point already on walkable ground is
        /// returned unchanged; a point outside it (across a gap, up an unclimbable slope, or
        /// anywhere local steering drifted to) is pulled to the nearest point on the nearest
        /// walkable polygon.
        /// <para>
        /// Implementations that carry no surface-based domain (for example a node-graph-only
        /// service) report false and leave the requested position untouched; callers must treat
        /// that as "no correction available", never as "position is valid".
        /// </para>
        /// </summary>
        bool TrySnapToNavigationSurface(string agentTypeId, int worldXcm, int worldZcm, out int snappedXcm, out int snappedZcm)
        {
            snappedXcm = worldXcm;
            snappedZcm = worldZcm;
            return false;
        }
    }
}

