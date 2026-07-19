using System;
using Arch.Core;
using Ludots.Core.Navigation.GraphCore;
using Ludots.Core.Navigation.GraphEcs;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Features.GraphRuntime
{
    [TestFixture]
    public sealed class GraphPathEcsTests
    {
        [Test]
        public void GraphPathfindingSystem_PathBeyondInlineCapacity_ReportsExplicitFailure()
        {
            using var world = World.Create();
            NodeGraph graph = BuildLineGraph(GraphPathBuffer.Capacity + 1);
            Entity request = world.Create(
                new GraphPathRequest(0, graph.NodeCount - 1, GraphPathMode.PreciseAStar),
                default(GraphPathBuffer),
                default(GraphPathResultComponent));
            var system = new GraphPathfindingSystem(world, graph);

            system.Update(0f);

            GraphPathResult result = world.Get<GraphPathResultComponent>(request).Result;
            Assert.That(result.Status, Is.EqualTo(GraphPathStatus.BufferTooSmall));
            Assert.That(result.RequiredNodeCount, Is.EqualTo(GraphPathBuffer.Capacity + 1));
            Assert.That(world.Get<GraphPathBuffer>(request).Count, Is.EqualTo(0));
            Assert.That(system.BufferTooSmallCount, Is.EqualTo(1));
            Assert.That(system.LastRequiredNodeCount, Is.EqualTo(GraphPathBuffer.Capacity + 1));
        }

        [Test]
        public void GraphPathfindingSystem_SteadyStateAllocatesZero()
        {
            using var world = World.Create();
            NodeGraph graph = BuildLineGraph(16);
            Entity request = world.Create(
                new GraphPathRequest(0, graph.NodeCount - 1, GraphPathMode.PreciseAStar),
                default(GraphPathBuffer),
                default(GraphPathResultComponent));
            var system = new GraphPathfindingSystem(world, graph);

            for (int i = 0; i < 8; i++)
            {
                system.Update(0f);
            }

            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2_000; i++)
            {
                system.Update(0f);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            ref GraphPathBuffer buffer = ref world.Get<GraphPathBuffer>(request);
            Assert.That(allocated, Is.LessThanOrEqualTo(64));
            Assert.That(buffer.Count, Is.EqualTo(graph.NodeCount));
            Assert.That(buffer.Nodes[0], Is.EqualTo(0));
            Assert.That(buffer.Nodes[buffer.Count - 1], Is.EqualTo(graph.NodeCount - 1));
        }

        private static NodeGraph BuildLineGraph(int nodeCount)
        {
            var builder = new NodeGraphBuilder(nodeCount, nodeCount - 1);
            for (int i = 0; i < nodeCount; i++)
            {
                builder.AddNode(i * 100, 0);
            }

            for (int i = 0; i < nodeCount - 1; i++)
            {
                builder.AddEdge(i, i + 1, 1f);
            }

            return builder.Build();
        }
    }
}
