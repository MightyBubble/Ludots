using Ludots.Core.Mathematics;

namespace Ludots.Core.Navigation.GraphCore
{
    public sealed class LinearScanNodeGraphSpatialIndex : INodeGraphSpatialIndex
    {
        private readonly NodeGraph _graph;

        public LinearScanNodeGraphSpatialIndex(NodeGraph graph)
        {
            _graph = graph ?? throw new System.ArgumentNullException(nameof(graph));
        }

        public GraphQueryResult QueryAabb(in WorldAabbCm bounds, Span<int> nodeIds)
        {
            int written = 0;
            int dropped = 0;

            var xs = _graph.PosXcm;
            var ys = _graph.PosYcm;
            int n = _graph.NodeCount;

            int left = bounds.Left;
            int top = bounds.Top;
            int right = bounds.Right;
            int bottom = bounds.Bottom;

            for (int i = 0; i < n; i++)
            {
                int x = xs[i];
                int y = ys[i];
                if (x < left || x > right || y < top || y > bottom) continue;

                if ((uint)written < (uint)nodeIds.Length) nodeIds[written++] = i;
                else dropped++;
            }

            return new GraphQueryResult(written, dropped);
        }

        public GraphQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<int> nodeIds)
        {
            if (radiusCm < 0) radiusCm = 0;
            int r2 = radiusCm * radiusCm;
            int written = 0;
            int dropped = 0;

            var xs = _graph.PosXcm;
            var ys = _graph.PosYcm;
            int n = _graph.NodeCount;

            int cx = center.X;
            int cy = center.Y;

            int minX = cx - radiusCm;
            int maxX = cx + radiusCm;
            int minY = cy - radiusCm;
            int maxY = cy + radiusCm;

            for (int i = 0; i < n; i++)
            {
                int x = xs[i];
                if (x < minX || x > maxX) continue;
                int y = ys[i];
                if (y < minY || y > maxY) continue;

                int dx = x - cx;
                int dy = y - cy;
                int d2 = dx * dx + dy * dy;
                if (d2 > r2) continue;

                if ((uint)written < (uint)nodeIds.Length) nodeIds[written++] = i;
                else dropped++;
            }

            return new GraphQueryResult(written, dropped);
        }

        public bool TryFindNearest(WorldCmInt2 position, int maxRadiusCm, out int nodeId, out int distSqCm)
        {
            if (maxRadiusCm < 0) maxRadiusCm = 0;
            int limitSq = maxRadiusCm == int.MaxValue ? int.MaxValue : maxRadiusCm * maxRadiusCm;

            int bestId = -1;
            int bestD2 = int.MaxValue;

            var xs = _graph.PosXcm;
            var ys = _graph.PosYcm;
            int n = _graph.NodeCount;

            int px = position.X;
            int py = position.Y;

            for (int i = 0; i < n; i++)
            {
                int dx = xs[i] - px;
                int dy = ys[i] - py;
                int d2 = dx * dx + dy * dy;
                if (d2 >= bestD2) continue;
                if (d2 > limitSq) continue;
                bestD2 = d2;
                bestId = i;
            }

            nodeId = bestId;
            distSqCm = bestD2;
            return bestId >= 0;
        }
    }
}

