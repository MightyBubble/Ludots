using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphSemantics.GAS;
using Ludots.Core.Navigation.GraphSemantics.GAS.Components;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Navigation.MultiLayerGraph;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class GraphNetworkTests
    {
        [Test]
        public void TagSetRegistry_Dedups_And_FilterMatches()
        {
            var reg = new GraphTagSetRegistry();
            ushort a = reg.GetOrAddFromTagIds(stackalloc[] { 1, 2, 3 });
            ushort b = reg.GetOrAddFromTagIds(stackalloc[] { 3, 2, 1 });
            That(a, Is.EqualTo(b));

            var filter = GraphTagFilterBuilder.Compile(stackalloc[] { 2 }, stackalloc[] { 9 });
            var pool = reg.Pool;
            That(filter.Matches(in pool[a]), Is.True);

            ushort c = reg.GetOrAddFromTagIds(stackalloc[] { 2, 9 });
            That(filter.Matches(in pool[c]), Is.False);
        }

        [Test]
        public void EdgeOverlaySink_WritesOverlayArrays()
        {
            var world = World.Create();
            try
            {
                var overlay = new GraphEdgeCostOverlay();
                overlay.EnsureCapacity(edgeCount: 3);
                overlay.CostAdd[1] = 123f;
                overlay.CostMul[1] = 456f;
                overlay.Blocked[1] = 1;

                var e = world.Create(new AttributeBuffer(), new GraphEdgeRef(edgeId: 1));
                world.Get<AttributeBuffer>(e).SetCurrent(0, 5f);
                world.Get<AttributeBuffer>(e).SetCurrent(1, 0.25f);
                world.Get<AttributeBuffer>(e).SetCurrent(2, 1f);

                var sink = new GraphEdgeCostOverlaySink(overlay);
                var entries = new[]
                {
                    new AttributeBindingEntry(attributeId: 0, sinkId: 0, channel: 0, mode: AttributeBindingMode.Override, resetPolicy: AttributeBindingResetPolicy.ResetToZeroPerLogicFrame, scale: 1f),
                    new AttributeBindingEntry(attributeId: 1, sinkId: 0, channel: 1, mode: AttributeBindingMode.Override, resetPolicy: AttributeBindingResetPolicy.ResetToZeroPerLogicFrame, scale: 1f),
                    new AttributeBindingEntry(attributeId: 2, sinkId: 0, channel: 2, mode: AttributeBindingMode.Override, resetPolicy: AttributeBindingResetPolicy.ResetToZeroPerLogicFrame, scale: 1f),
                };

                sink.Apply(world, entries, start: 0, count: entries.Length);

                That(overlay.CostAdd[1], Is.EqualTo(5f));
                That(overlay.CostMul[1], Is.EqualTo(0.25f));
                That(overlay.Blocked[1], Is.EqualTo(1));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void AStar_IsZeroAlloc_AfterWarmup()
        {
            var b = new NodeGraphBuilder(initialNodeCapacity: 8, initialEdgeCapacity: 16);
            int n0 = b.AddNode(0, 0);
            int n1 = b.AddNode(100, 0);
            int n2 = b.AddNode(200, 0);
            b.AddEdge(n0, n1, 1f);
            b.AddEdge(n1, n2, 1f);
            b.AddEdge(n0, n2, 3f);
            var g = b.Build();

            var scratch = new NodeGraphPathScratch();
            var policy = new DefaultTraversalPolicy();
            Span<int> path = stackalloc int[64];

            NodeGraphPathService.FindPathAStar(g, n0, n2, path, ref scratch, ref policy);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            long baseline = after - before;

            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++)
            {
                NodeGraphPathService.FindPathAStar(g, n0, n2, path, ref scratch, ref policy);
            }
            after = GC.GetAllocatedBytesForCurrentThread();
            That(after - before - baseline, Is.LessThanOrEqualTo(64));
        }

        [Test]
        public void ChunkedStore_BuildsLoadedView_WithCrossChunkEdges()
        {
            var store = new ChunkedNodeGraphStore();

            long aKey = GraphChunkKey.Pack(0, 0);
            long bKey = GraphChunkKey.Pack(1, 0);

            var aBuilder = new NodeGraphBuilder(2, 1);
            aBuilder.AddNode(0, 0);
            aBuilder.AddNode(100, 0);
            aBuilder.AddEdge(0, 1, 1f);
            store.AddOrReplace(aKey, new GraphChunkData(aBuilder.Build(), new[]
            {
                new GraphCrossEdge(fromLocalNodeId: 1, toChunkKey: bKey, toLocalNodeId: 0, baseCost: 1f, tagSetId: 0)
            }));

            var bBuilder = new NodeGraphBuilder(1, 0);
            bBuilder.AddNode(200, 0);
            store.AddOrReplace(bKey, new GraphChunkData(bBuilder.Build(), Array.Empty<GraphCrossEdge>()));

            var view = store.BuildLoadedView();
            var graph = view.Graph;

            That(graph.NodeCount, Is.EqualTo(3));
            That(graph.EdgeCount, Is.EqualTo(2));

            var scratch = new NodeGraphPathScratch();
            var policy = new DefaultTraversalPolicy();
            Span<int> path = stackalloc int[8];
            var r = NodeGraphPathService.FindPathAStar(graph, 0, 2, path, ref scratch, ref policy);
            That(r.Status, Is.EqualTo(GraphPathStatus.Success));
            That(r.NodeCount, Is.EqualTo(3));
        }

        [Test]
        public void ChunkedStore_BuildsCorridorView_OnlyIncludingSelectedChunks()
        {
            var store = new ChunkedNodeGraphStore();

            long aKey = GraphChunkKey.Pack(0, 0);
            long bKey = GraphChunkKey.Pack(1, 0);

            var aBuilder = new NodeGraphBuilder(2, 1);
            aBuilder.AddNode(0, 0);
            aBuilder.AddNode(100, 0);
            aBuilder.AddEdge(0, 1, 1f);
            store.AddOrReplace(aKey, new GraphChunkData(aBuilder.Build(), new[]
            {
                new GraphCrossEdge(fromLocalNodeId: 1, toChunkKey: bKey, toLocalNodeId: 0, baseCost: 1f, tagSetId: 0)
            }));

            var bBuilder = new NodeGraphBuilder(1, 0);
            bBuilder.AddNode(200, 0);
            store.AddOrReplace(bKey, new GraphChunkData(bBuilder.Build(), Array.Empty<GraphCrossEdge>()));

            var onlyA = store.BuildLoadedView(new[] { aKey });
            That(onlyA.Graph.NodeCount, Is.EqualTo(2));
            That(onlyA.Graph.EdgeCount, Is.EqualTo(1));

            var corridorKeys = GraphCorridorChunkSelector.Expand(new[] { aKey, bKey }, radius: 0);
            var corridor = store.BuildLoadedView(corridorKeys);
            That(corridor.Graph.NodeCount, Is.EqualTo(3));
            That(corridor.Graph.EdgeCount, Is.EqualTo(2));
        }

        [Test]
        public void RouteTable_CostMatches_AStarPathCost()
        {
            var rnd = new Random(123);
            var b = new NodeGraphBuilder(initialNodeCapacity: 32, initialEdgeCapacity: 256);
            for (int i = 0; i < 20; i++)
            {
                b.AddNode(i * 10, i * 7);
            }
            for (int i = 0; i < 20; i++)
            {
                for (int j = 0; j < 20; j++)
                {
                    if (i == j) continue;
                    if (rnd.NextDouble() < 0.3) continue;
                    b.AddEdge(i, j, (float)(1 + rnd.NextDouble() * 5));
                }
            }
            var g = b.Build();
            var policy = new ZeroHeuristicPolicy();
            var table = GraphRouteTableBuilder.BuildAllPairsShortestPaths(g, ref policy);

            var scratch = new NodeGraphPathScratch();
            Span<int> path = stackalloc int[64];

            for (int k = 0; k < 25; k++)
            {
                int s = rnd.Next(0, 20);
                int t = rnd.Next(0, 20);
                if (s == t) continue;

                bool hasCost = table.TryGetCost(s, t, out float cost);
                var r = NodeGraphPathService.FindPathAStar(g, s, t, path, ref scratch, ref policy);
                if (!hasCost)
                {
                    That(r.Status, Is.Not.EqualTo(GraphPathStatus.Success));
                    continue;
                }

                That(r.Status, Is.EqualTo(GraphPathStatus.Success));
                float pathCost = SumPathCost(g, path.Slice(0, r.NodeCount));
                That(pathCost, Is.EqualTo(cost).Within(1e-4f));
            }
        }

        private struct ZeroHeuristicPolicy : INodeGraphTraversalPolicy
        {
            public bool IsNodeAllowed(int nodeId) => true;
            public bool IsEdgeAllowed(int edgeIndex, int fromNode, int toNode) => true;
            public float GetEdgeCost(int edgeIndex, float baseCost) => baseCost;
            public float GetHeuristic(int nodeId, int goalNodeId, int dxCm, int dyCm) => 0f;
        }

        [Test]
        public void HierarchicalPathService_RefinesViaGateways()
        {
            var coarseBuilder = new NodeGraphBuilder(2, 2);
            coarseBuilder.AddNode(0, 0);
            coarseBuilder.AddNode(1000, 0);
            coarseBuilder.AddEdge(0, 1, 1f);
            coarseBuilder.AddEdge(1, 0, 1f);
            var coarse = coarseBuilder.Build();

            var fineBuilder = new NodeGraphBuilder(4, 6);
            fineBuilder.AddNode(0, 0);
            fineBuilder.AddNode(100, 0);
            fineBuilder.AddNode(900, 0);
            fineBuilder.AddNode(1000, 0);
            fineBuilder.AddEdge(0, 1, 1f);
            fineBuilder.AddEdge(1, 2, 1f);
            fineBuilder.AddEdge(2, 3, 1f);
            fineBuilder.AddEdge(1, 0, 1f);
            fineBuilder.AddEdge(2, 1, 1f);
            fineBuilder.AddEdge(3, 2, 1f);
            var fine = fineBuilder.Build();

            var mapBuilder = new InterLayerMappingBuilder();
            mapBuilder.Reset(fineNodeCount: fine.NodeCount, coarseNodeCount: coarse.NodeCount);
            mapBuilder.SetParent(0, 0);
            mapBuilder.SetParent(1, 0);
            mapBuilder.SetParent(2, 1);
            mapBuilder.SetParent(3, 1);
            mapBuilder.AddGateway(0, 1);
            mapBuilder.AddGateway(1, 2);
            var mapping = mapBuilder.Build();

            var ml = new MultiLayerGraph(new[] { coarse, fine }, new[] { null, mapping });

            var fineScratch = new NodeGraphPathScratch();
            var coarseScratch = new NodeGraphPathScratch();
            var finePolicy = new DefaultTraversalPolicy();
            var coarsePolicy = new DefaultTraversalPolicy();
            Span<int> outPath = stackalloc int[32];

            var r = HierarchicalPathService.FindPathHierarchical(ml, fineLayerIndex: 1, startFineNodeId: 0, goalFineNodeId: 3, outPath, ref fineScratch, ref coarseScratch, ref finePolicy, ref coarsePolicy);
            That(r.Status, Is.EqualTo(GraphPathStatus.Success));
            That(outPath[0], Is.EqualTo(0));
            That(outPath[r.NodeCount - 1], Is.EqualTo(3));
        }

        private static float SumPathCost(NodeGraph g, ReadOnlySpan<int> path)
        {
            float sum = 0f;
            var edgeStart = g.EdgeStart;
            var edgeTo = g.EdgeTo;
            var edgeCost = g.EdgeBaseCost;

            for (int i = 0; i < path.Length - 1; i++)
            {
                int from = path[i];
                int to = path[i + 1];
                float best = float.PositiveInfinity;
                for (int e = edgeStart[from]; e < edgeStart[from + 1]; e++)
                {
                    if (edgeTo[e] == to)
                    {
                        float c = edgeCost[e];
                        if (c < best) best = c;
                    }
                }
                sum += best;
            }
            return sum;
        }
    }
}
