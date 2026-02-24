using System;
using Ludots.Core.Collections;

namespace Ludots.Core.Navigation.GraphCore
{
    public static class GraphRouteTableBuilder
    {
        public static GraphRouteTable BuildAllPairsShortestPaths<TPolicy>(NodeGraph graph, ref TPolicy policy)
            where TPolicy : struct, INodeGraphTraversalPolicy
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            int n = graph.NodeCount;
            if (n > ushort.MaxValue) throw new InvalidOperationException("RouteTable supports up to 65535 nodes.");

            var nextHop = new ushort[n * n];
            var cost = new float[n * n];
            Array.Fill(nextHop, ushort.MaxValue);
            for (int i = 0; i < cost.Length; i++) cost[i] = float.PositiveInfinity;

            var dist = n == 0 ? Array.Empty<float>() : new float[n];
            var parent = n == 0 ? Array.Empty<int>() : new int[n];
            var closedStamp = n == 0 ? Array.Empty<int>() : new int[n];
            int stamp = 1;

            var pq = new PriorityQueue<int>(64);

            for (int src = 0; src < n; src++)
            {
                if (!policy.IsNodeAllowed(src)) continue;

                stamp++;
                if (stamp == int.MaxValue)
                {
                    Array.Clear(closedStamp, 0, closedStamp.Length);
                    stamp = 1;
                }

                for (int i = 0; i < n; i++)
                {
                    dist[i] = float.PositiveInfinity;
                    parent[i] = -1;
                }
                pq.Clear();

                dist[src] = 0f;
                pq.Enqueue(src, 0f);

                while (pq.TryDequeue(out int u, out float du))
                {
                    if (closedStamp[u] == stamp) continue;
                    closedStamp[u] = stamp;
                    if (du > dist[u]) continue;

                    var range = graph.GetOutgoingEdges(u);
                    for (int e = range.Start; e < range.EndExclusive; e++)
                    {
                        int v = graph.EdgeTo[e];
                        if (!policy.IsNodeAllowed(v)) continue;
                        if (!policy.IsEdgeAllowed(e, u, v)) continue;

                        float edgeCost = policy.GetEdgeCost(e, graph.EdgeBaseCost[e]);
                        if (edgeCost < 0f || float.IsNaN(edgeCost)) continue;

                        float nd = dist[u] + edgeCost;
                        if (nd < dist[v])
                        {
                            dist[v] = nd;
                            parent[v] = u;
                            pq.Enqueue(v, nd);
                        }
                    }
                }

                for (int dst = 0; dst < n; dst++)
                {
                    int idx = src * n + dst;
                    cost[idx] = dist[dst];
                    if (dst == src)
                    {
                        nextHop[idx] = (ushort)src;
                        continue;
                    }

                    if (parent[dst] < 0) continue;

                    int cur = dst;
                    int p = parent[cur];
                    while (p >= 0 && p != src)
                    {
                        cur = p;
                        p = parent[cur];
                    }
                    if (p == src)
                    {
                        nextHop[idx] = (ushort)cur;
                    }
                }
            }

            return new GraphRouteTable(n, nextHop, cost);
        }
    }
}

