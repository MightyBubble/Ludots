using Ludots.Core.Mathematics;

namespace Ludots.Core.Navigation.GraphCore
{
    public interface INodeGraphSpatialIndex
    {
        GraphQueryResult QueryAabb(in WorldAabbCm bounds, Span<int> nodeIds);

        GraphQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<int> nodeIds);

        bool TryFindNearest(WorldCmInt2 position, int maxRadiusCm, out int nodeId, out int distSqCm);
    }
}

