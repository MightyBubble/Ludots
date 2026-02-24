using System;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Navigation.GraphCore
{
    public sealed class UniformGridNodeGraphSpatialIndex : INodeGraphSpatialIndex
    {
        private readonly NodeGraph _graph;
        private readonly int _cellSizeCm;
        private readonly int _minX;
        private readonly int _minY;
        private readonly int _cellsX;
        private readonly int _cellsY;
        private readonly int[] _cellHead;
        private readonly int[] _nodeNext;

        public UniformGridNodeGraphSpatialIndex(NodeGraph graph, int cellSizeCm)
        {
            if (cellSizeCm <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeCm));
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _cellSizeCm = cellSizeCm;

            int n = graph.NodeCount;
            if (n == 0)
            {
                _minX = 0;
                _minY = 0;
                _cellsX = 0;
                _cellsY = 0;
                _cellHead = Array.Empty<int>();
                _nodeNext = Array.Empty<int>();
                return;
            }

            var xs = graph.PosXcm;
            var ys = graph.PosYcm;
            int minX = xs[0];
            int maxX = xs[0];
            int minY = ys[0];
            int maxY = ys[0];
            for (int i = 1; i < n; i++)
            {
                int x = xs[i];
                int y = ys[i];
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            _minX = minX;
            _minY = minY;
            _cellsX = ((maxX - minX) / _cellSizeCm) + 1;
            _cellsY = ((maxY - minY) / _cellSizeCm) + 1;

            int cellCount = _cellsX * _cellsY;
            _cellHead = new int[cellCount];
            Array.Fill(_cellHead, -1);
            _nodeNext = new int[n];
            Array.Fill(_nodeNext, -1);

            for (int i = 0; i < n; i++)
            {
                int cell = GetCellIndexUnsafe(xs[i], ys[i]);
                _nodeNext[i] = _cellHead[cell];
                _cellHead[cell] = i;
            }
        }

        public GraphQueryResult QueryAabb(in WorldAabbCm bounds, Span<int> nodeIds)
        {
            if (_graph.NodeCount == 0) return default;

            int written = 0;
            int dropped = 0;

            int minCellX = ClampCellX((bounds.Left - _minX) / _cellSizeCm);
            int maxCellX = ClampCellX((bounds.Right - _minX) / _cellSizeCm);
            int minCellY = ClampCellY((bounds.Top - _minY) / _cellSizeCm);
            int maxCellY = ClampCellY((bounds.Bottom - _minY) / _cellSizeCm);

            var xs = _graph.PosXcm;
            var ys = _graph.PosYcm;

            int left = bounds.Left;
            int right = bounds.Right;
            int top = bounds.Top;
            int bottom = bounds.Bottom;

            for (int cy = minCellY; cy <= maxCellY; cy++)
            {
                int row = cy * _cellsX;
                for (int cx = minCellX; cx <= maxCellX; cx++)
                {
                    int node = _cellHead[row + cx];
                    while (node != -1)
                    {
                        int x = xs[node];
                        int y = ys[node];
                        if (x >= left && x <= right && y >= top && y <= bottom)
                        {
                            if ((uint)written < (uint)nodeIds.Length) nodeIds[written++] = node;
                            else dropped++;
                        }
                        node = _nodeNext[node];
                    }
                }
            }

            return new GraphQueryResult(written, dropped);
        }

        public GraphQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<int> nodeIds)
        {
            if (_graph.NodeCount == 0) return default;
            if (radiusCm < 0) radiusCm = 0;

            int written = 0;
            int dropped = 0;

            int cx = center.X;
            int cy = center.Y;
            int r2 = radiusCm * radiusCm;

            int minX = cx - radiusCm;
            int maxX = cx + radiusCm;
            int minY = cy - radiusCm;
            int maxY = cy + radiusCm;

            int minCellX = ClampCellX((minX - _minX) / _cellSizeCm);
            int maxCellX = ClampCellX((maxX - _minX) / _cellSizeCm);
            int minCellY = ClampCellY((minY - _minY) / _cellSizeCm);
            int maxCellY = ClampCellY((maxY - _minY) / _cellSizeCm);

            var xs = _graph.PosXcm;
            var ys = _graph.PosYcm;

            for (int cyCell = minCellY; cyCell <= maxCellY; cyCell++)
            {
                int row = cyCell * _cellsX;
                for (int cxCell = minCellX; cxCell <= maxCellX; cxCell++)
                {
                    int node = _cellHead[row + cxCell];
                    while (node != -1)
                    {
                        int x = xs[node];
                        if (x >= minX && x <= maxX)
                        {
                            int y = ys[node];
                            if (y >= minY && y <= maxY)
                            {
                                int dx = x - cx;
                                int dy = y - cy;
                                int d2 = dx * dx + dy * dy;
                                if (d2 <= r2)
                                {
                                    if ((uint)written < (uint)nodeIds.Length) nodeIds[written++] = node;
                                    else dropped++;
                                }
                            }
                        }
                        node = _nodeNext[node];
                    }
                }
            }

            return new GraphQueryResult(written, dropped);
        }

        public bool TryFindNearest(WorldCmInt2 position, int maxRadiusCm, out int nodeId, out int distSqCm)
        {
            if (_graph.NodeCount == 0)
            {
                nodeId = -1;
                distSqCm = 0;
                return false;
            }

            if (maxRadiusCm < 0) maxRadiusCm = 0;

            int px = position.X;
            int py = position.Y;
            int bestId = -1;
            int bestD2 = int.MaxValue;
            int limitSq = maxRadiusCm == int.MaxValue ? int.MaxValue : maxRadiusCm * maxRadiusCm;

            int cx = (px - _minX) / _cellSizeCm;
            int cy = (py - _minY) / _cellSizeCm;
            cx = ClampCellX(cx);
            cy = ClampCellY(cy);

            int maxRing = maxRadiusCm == int.MaxValue
                ? Math.Max(_cellsX, _cellsY)
                : (maxRadiusCm / _cellSizeCm) + 1;

            var xs = _graph.PosXcm;
            var ys = _graph.PosYcm;

            for (int ring = 0; ring <= maxRing; ring++)
            {
                int minXCell = ClampCellX(cx - ring);
                int maxXCell = ClampCellX(cx + ring);
                int minYCell = ClampCellY(cy - ring);
                int maxYCell = ClampCellY(cy + ring);

                bool anyVisited = false;
                for (int yCell = minYCell; yCell <= maxYCell; yCell++)
                {
                    int row = yCell * _cellsX;
                    for (int xCell = minXCell; xCell <= maxXCell; xCell++)
                    {
                        if (ring != 0)
                        {
                            bool border = yCell == minYCell || yCell == maxYCell || xCell == minXCell || xCell == maxXCell;
                            if (!border) continue;
                        }

                        int node = _cellHead[row + xCell];
                        while (node != -1)
                        {
                            anyVisited = true;
                            int dx = xs[node] - px;
                            int dy = ys[node] - py;
                            int d2 = dx * dx + dy * dy;
                            if (d2 < bestD2 && d2 <= limitSq)
                            {
                                bestD2 = d2;
                                bestId = node;
                            }
                            node = _nodeNext[node];
                        }
                    }
                }

                if (bestId >= 0 && anyVisited)
                {
                    int ringCm = ring * _cellSizeCm;
                    if (ringCm * ringCm > bestD2) break;
                }
            }

            nodeId = bestId;
            distSqCm = bestD2;
            return bestId >= 0;
        }

        private int GetCellIndexUnsafe(int xCm, int yCm)
        {
            int cx = (xCm - _minX) / _cellSizeCm;
            int cy = (yCm - _minY) / _cellSizeCm;
            return cy * _cellsX + cx;
        }

        private int ClampCellX(int cx)
        {
            if (cx < 0) return 0;
            int max = _cellsX - 1;
            if (cx > max) return max;
            return cx;
        }

        private int ClampCellY(int cy)
        {
            if (cy < 0) return 0;
            int max = _cellsY - 1;
            if (cy > max) return max;
            return cy;
        }
    }
}

