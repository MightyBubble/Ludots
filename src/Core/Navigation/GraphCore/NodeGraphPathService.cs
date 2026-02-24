using System;

namespace Ludots.Core.Navigation.GraphCore
{
    public static class NodeGraphPathService
    {
        public static GraphPathResult FindPathAStar<TPolicy>(
            NodeGraph graph,
            int startNodeId,
            int goalNodeId,
            Span<int> outPathNodeIds,
            ref NodeGraphPathScratch scratch,
            ref TPolicy policy,
            int maxExpanded = int.MaxValue)
            where TPolicy : struct, INodeGraphTraversalPolicy
        {
            if (graph == null) return new GraphPathResult(GraphPathStatus.InvalidInput, 0, 0, 0);
            if ((uint)startNodeId >= (uint)graph.NodeCount) return new GraphPathResult(GraphPathStatus.InvalidInput, 0, 0, 0);
            if ((uint)goalNodeId >= (uint)graph.NodeCount) return new GraphPathResult(GraphPathStatus.InvalidInput, 0, 0, 0);
            if (!policy.IsNodeAllowed(startNodeId) || !policy.IsNodeAllowed(goalNodeId))
            {
                return new GraphPathResult(GraphPathStatus.NotFound, 0, 0, 0);
            }

            scratch ??= new NodeGraphPathScratch();
            scratch.EnsureCapacity(graph.NodeCount);
            int stamp = scratch.Begin();

            scratch.SetStart(startNodeId, stamp);

            var xs = graph.PosXcm;
            var ys = graph.PosYcm;

            int sx = xs[startNodeId];
            int sy = ys[startNodeId];
            int gx = xs[goalNodeId];
            int gy = ys[goalNodeId];
            float h0 = policy.GetHeuristic(startNodeId, goalNodeId, sx - gx, sy - gy);
            scratch.EnqueueOpen(startNodeId, h0);

            int expanded = 0;
            int foundNode = -1;

            while (scratch.TryDequeueOpen(out int current, out _))
            {
                if (scratch.IsClosed(current, stamp)) continue;
                scratch.MarkClosed(current, stamp);

                if (current == goalNodeId)
                {
                    foundNode = current;
                    break;
                }

                expanded++;
                if (expanded > maxExpanded)
                {
                    return new GraphPathResult(GraphPathStatus.OverBudget, 0, 0, expanded);
                }

                float gCurrent = scratch.GetG(current);

                var range = graph.GetOutgoingEdges(current);
                for (int e = range.Start; e < range.EndExclusive; e++)
                {
                    int to = graph.EdgeTo[e];
                    if (!policy.IsNodeAllowed(to)) continue;
                    if (!policy.IsEdgeAllowed(e, current, to)) continue;

                    float edgeCost = policy.GetEdgeCost(e, graph.EdgeBaseCost[e]);
                    if (edgeCost < 0f || float.IsNaN(edgeCost)) continue;

                    float tentative = gCurrent + edgeCost;
                    bool seen = scratch.IsSeen(to, stamp);
                    if (!seen || tentative < scratch.GetG(to))
                    {
                        scratch.Relax(to, stamp, tentative, current, e);

                        int dx = xs[to] - gx;
                        int dy = ys[to] - gy;
                        float h = policy.GetHeuristic(to, goalNodeId, dx, dy);
                        float f = tentative + h;
                        scratch.EnqueueOpen(to, f);
                    }
                }
            }

            if (foundNode < 0)
            {
                return new GraphPathResult(GraphPathStatus.NotFound, 0, 0, expanded);
            }

            int required = 1;
            for (int n = goalNodeId; n != startNodeId;)
            {
                int p = scratch.GetParentNode(n);
                if (p < 0)
                {
                    return new GraphPathResult(GraphPathStatus.NotFound, 0, 0, expanded);
                }
                required++;
                n = p;
            }

            if (outPathNodeIds.Length < required)
            {
                return new GraphPathResult(GraphPathStatus.BufferTooSmall, 0, required, expanded);
            }

            int write = required - 1;
            int cur = goalNodeId;
            outPathNodeIds[write--] = cur;
            while (cur != startNodeId)
            {
                cur = scratch.GetParentNode(cur);
                outPathNodeIds[write--] = cur;
            }

            return new GraphPathResult(GraphPathStatus.Success, required, required, expanded, scratch.GetG(goalNodeId));
        }
    }
}
